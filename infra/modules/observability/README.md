# observability

Prometheus and Grafana as Container Apps in the app's own environment, with their data on an NFS share.

- **Prometheus** (internal ingress only) receives the app's metrics, which the app pushes over OTLP
  (`/api/v1/otlp/v1/metrics`, HTTP Basic authentication), and scrapes the Azure Monitor exporter beside it for platform
  metrics: the Container Apps' replicas, restarts, CPU and memory, the PostgreSQL server, and the share's used
  capacity. The exporter signs in as Prometheus' user-assigned identity, which has Monitoring Reader on the app's
  resource group.
- **Grafana** is public at `grafana_hostname` through Cloudflare, like the app's instances: a TCP ingress port of its
  own, Cloudflare-only IP restrictions, and TLS in the container with the same origin certificate. GitHub is the only
  way in, for the users in `grafana_github_admins` and `grafana_github_viewers`. Its data source and the dashboards
  in `dashboards/` are provisioned and read-only in the UI.
- **Storage**: a Premium FileStorage account with one NFS share, which admits only the Container Apps subnet.
  Prometheus keeps its database in `prometheus/`, Grafana its SQLite database in `grafana/`.

Container Apps can't mount Azure Disks. Of what it can mount, NFS is the POSIX file system Prometheus' database
needs; SMB is not. Two things follow:

- **Secure transfer is off on the storage account.** Container Apps can't encrypt NFS in transit, and Azure refuses
  the mount while "secure transfer required" is on. The account accepts no keys and only the Container Apps subnet.
- **One writer at a time.** A new revision starts before the old one stops. Prometheus' start script waits for the old
  replica to release its database lock, and its probes watch the exporter's port so that the waiting replica still
  counts as ready (otherwise neither would ever give way).

Configuration reaches the containers as environment variables, which an init container (Azure Linux) writes to disk:
`prometheus.yml` and the web configuration for Prometheus, the provisioning files and the PEM certificate for Grafana.
A change to either redeploys that app. To change a dashboard, edit its JSON here (or export it from Grafana) and
apply.

<!-- BEGIN_TF_DOCS -->
<!-- END_TF_DOCS -->
