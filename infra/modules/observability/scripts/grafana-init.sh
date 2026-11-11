#!/usr/bin/env bash
# Grafana's init container (Azure Linux, as root, with openssl), which runs before Grafana on every start of a
# replica:
#
#   - turns the Cloudflare origin certificate from Key Vault (a Base64 PFX without a password, the format the app
#     loads too) into the PEM files Grafana serves HTTPS with, readable by Grafana's user only;
#   - writes the provisioning Terraform passes in as environment variables: the Prometheus data source, the dashboard
#     provider and each dashboard (GRAFANA_DASHBOARD_JSON_<NAME>, gzip-compressed and Base64-encoded);
#   - gives Grafana's user (472, group root) its directory on the NFS share, whose root belongs to root.
#
# Mounts: the TLS volume at /tls, the provisioning volume at /provisioning, the whole share at /data.
set -euo pipefail

readonly grafana_user=472
readonly grafana_group=0

: "${ORIGIN_CERTIFICATE_PFX_BASE64:?}" "${GRAFANA_DATASOURCES:?}" "${GRAFANA_DASHBOARD_PROVIDERS:?}" "${GRAFANA_DATA_DIR:?}"

umask 0077
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# --- TLS ---------------------------------------------------------------------------------------------------------
printf '%s' "$ORIGIN_CERTIFICATE_PFX_BASE64" | base64 --decode > "$work/origin.pfx"
unset ORIGIN_CERTIFICATE_PFX_BASE64

# OpenSSL 3 reads a PFX protected with older algorithms only with -legacy; try the defaults first.
pkcs12() {
  openssl pkcs12 -in "$work/origin.pfx" -passin pass: "$@" 2> /dev/null \
    || openssl pkcs12 -legacy -in "$work/origin.pfx" -passin pass: "$@"
}

# The server certificate first, then whatever chain the PFX carries.
pkcs12 -clcerts -nokeys | openssl x509 -out "$work/certificate.pem"
pkcs12 -cacerts -nokeys -out "$work/chain.pem"
cat "$work/certificate.pem" "$work/chain.pem" > /tls/origin.crt
pkcs12 -nocerts -nodes | openssl pkey -out /tls/origin.key

chown "$grafana_user:$grafana_group" /tls/origin.crt /tls/origin.key
chmod 0444 /tls/origin.crt
chmod 0400 /tls/origin.key

# --- Provisioning ------------------------------------------------------------------------------------------------
install -d -o "$grafana_user" -g "$grafana_group" -m 0750 \
  /provisioning/datasources /provisioning/dashboards /provisioning/dashboards-json \
  /provisioning/plugins /provisioning/alerting
printf '%s\n' "$GRAFANA_DATASOURCES" > /provisioning/datasources/prometheus.yaml
printf '%s\n' "$GRAFANA_DASHBOARD_PROVIDERS" > /provisioning/dashboards/nibbles.yaml

dashboards=0
for variable in $(compgen -e); do
  [[ "$variable" == GRAFANA_DASHBOARD_JSON_* ]] || continue
  name="${variable#GRAFANA_DASHBOARD_JSON_}"
  name="${name,,}"
  name="${name//_/-}"
  printf '%s' "${!variable}" | base64 --decode | gzip --decompress > "/provisioning/dashboards-json/$name.json"
  dashboards=$((dashboards + 1))
done

chown -R "$grafana_user:$grafana_group" /provisioning
find /provisioning -type f -exec chmod 0440 {} +

# --- Data --------------------------------------------------------------------------------------------------------
# Idempotent: on later starts it only resets the directory's owner and mode, not the database inside.
install -d -o "$grafana_user" -g "$grafana_group" -m 0750 "/data/$GRAFANA_DATA_DIR"

echo "Prepared Grafana's certificate, $dashboards dashboards and /data/$GRAFANA_DATA_DIR."
