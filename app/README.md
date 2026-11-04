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