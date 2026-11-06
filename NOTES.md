CONNECTING TO DB THROUGH A CONTAINER IN THE VNET

Neither your laptop nor anything else on the internet can reach the database: it has a private IP inside the VNet and no public endpoint. So getting the password is only half the job. You also need a machine inside the VNet to run psql from.

1. Get the credentials from Key Vault

Terraform stores both database passwords in Key Vault, and your account can read them because Terraform gave you the Key Vault Secrets Officer role.

┌───────┬────────────────┬───────────────────────────┬───────────────────────────────┐
│ Login │   User name    │     Key Vault secret      │          Use it for           │
├───────┼────────────────┼───────────────────────────┼───────────────────────────────┤
│ Admin │ loginapp_admin │ postgresql-admin-password │ Managing roles and the server │
├───────┼────────────────┼──────────────────────────────┤
│ App   │ loginapp_app   │ postgresql-app-password   │ Querying the app's tables     │
└───────┴────────────────┴───────────────────────────┴───────────────────────────────┘                           
Use the app login for queries. The app creates its tables with that login, so it owns them. The Azure admin isn't a real PostgreSQL superuser and by defaulher role owns.
cd ~/login-app/infra
RG=$(terraform output -raw resource_group
KV=$(terraform output -raw key_vault_name)
CAE=$(terraform output -raw container_appPGHOST=$(terraform output -raw postgresql_server_fqdn)
echo "$PGHOST"
az keyvault secret show --vault-name "$KV" --name postgresql-app-password --query value -o tsv

2. Run psql from inside the VNet
The simplest way is a temporary container in the same Container Apps environment as the app. The database's
firewall rules already let that subnet co below is the one the database setup jobuses; it includes psql and is pulled from Microsoft's public registry, so no registry login is needed.

az containerapp create --name ca-psql-con-environment "$CAE" \
  --image mcr.microsoft.com/azurelinux/base/postgres:16.15-1-azl3.0.20261027 \
  --command sleep --args infinity \                                                                               --cpu 0.25 --memory 0.5Gi --min-replicas 1 --max-replicas 1 \
  --workload-profile-name Consumption --tags project=loginapp purpose=psql-console

az containerapp exec --name ca-psql-console --resource-group "$RG" --command bash
Inside the container, connect with full TLS checking, replacing <PGHOST> with the address you printed earlier.
Paste the password when psql asks for it:
psql "host=<PGHOST> port=5432 dbname=loginapp user=loginapp_app sslmode=verify-full
sslrootcert=/etc/pki/tls/certs/ca-bundle.crt"

Some queries to start with:
                                                                                                                \dt                                   -- list tables
SELECT "Id", "Email", "LockoutEnd" FROM "AspNetUsers" LIMIT 20;
SELECT * FROM "__EFMigrationsHistory";

ASP.NET Core Identity and EF Core use mixs, so those need double quotes. This psqlis version 16 and the server is 18. Ordinary queries work fine, but a few \d commands may show a warning.

3. Delete the console when you're done

az containerapp delete --name ca-psql-console --resource-group "$RG" --yes                                     
Terraform doesn't manage this container, so creating and deleting it won't change your next plan. It costs a few
cents an hour while it's running.                                                                              
A few things to keep in mind:
- Anything you type into psql goes straight to the live database. For quick looks around, run BEGIN READ ONLY; first.The ConnectionStrings--DefaultConnection secret holds the app's complete connection string, if you'd rather
  copy it whole.
- If you'll do this often, a point-to-sit small VM would let you use a desktop toollike pgAdmin. Neither is in the Terraform yet; I can add one if you want.