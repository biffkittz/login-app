# Useful Scripts

Run container at localhost:8080 and pass in the required dev environment variables 
```bash
sudo docker run -p 8080:80 -e ConnectionStrings:DefaultConnection="Host=host.docker.internal;Port=5432;Database=loginapp;Username=loginapp;Password=loginapp_dev;Include Error Detail=true" -e Jwt:SigningKey=<Key> loginapp:latest
```

Build the container
```bash
cd ./login-app/app
sudo docker build -t loginapp .
```

Docker push to ACR

To make auth work, need to add user to docker group:
```bash
sudo groupadd docker
sudo usermod -aG docker $USER
newgrp docker
```
Then push:
```bash
az login
az acr login --name crloginappdemo85705a
docker tag loginapp:latest crloginappdemo85705a.azurecr.io/loginapp:v1
docker push crloginappdemo85705a.azurecr.io/loginapp:v1
```

Configuring auth for Azure remote backend for Terraform:
https://learn.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage?tabs=azure-cli
```bash
ACCOUNT_KEY=$(az storage account keys list --resource-group rg-terraform-state  --account-name demonstration --query '[0].value' -o tsv)
export ARM_ACCESS_KEY=$ACCOUNT_KEY
terraform init
```