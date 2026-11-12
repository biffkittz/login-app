#!/usr/bin/env bash
# Prometheus' init container (Azure Linux, as root), which runs before Prometheus on every start of a replica:
#
#   - gives Prometheus' user (nobody, 65534) its directory on the NFS share, whose root belongs to root;
#   - writes the configuration Terraform passes in as environment variables (prometheus.yml, plus web.yml and the
#     self-scrape password from Key Vault) to the replica's EmptyDir volume, readable by Prometheus' group only.
#
# Mounts: the whole share at /data, the configuration volume at /config.
set -euo pipefail

readonly prometheus_user=65534
readonly config=/config

: "${PROMETHEUS_CONFIG:?}" "${PROMETHEUS_WEB_CONFIG:?}" "${PROMETHEUS_SELF_SCRAPE_PASSWORD:?}" "${PROMETHEUS_DATA_DIR:?}"

# Idempotent: on later starts it only resets the directory's owner and mode, not what is inside.
install -d -o "$prometheus_user" -g "$prometheus_user" -m 0750 "/data/$PROMETHEUS_DATA_DIR"

umask 0077
printf '%s\n' "$PROMETHEUS_CONFIG" > "$config/prometheus.yml"
printf '%s\n' "$PROMETHEUS_WEB_CONFIG" > "$config/web.yml"
printf '%s' "$PROMETHEUS_SELF_SCRAPE_PASSWORD" > "$config/self-scrape-password"

chown "root:$prometheus_user" "$config" "$config/prometheus.yml" "$config/web.yml" "$config/self-scrape-password"
chmod 0750 "$config"
chmod 0440 "$config/prometheus.yml" "$config/web.yml" "$config/self-scrape-password"

echo "Prepared /data/$PROMETHEUS_DATA_DIR and Prometheus' configuration."
