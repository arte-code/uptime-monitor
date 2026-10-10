run:
  dotnet run

makemigrations +name:
    dotnet ef migrations add {{name}}

migrate:
    dotnet ef database update


up:
    docker compose up -d

down:
    docker compose down

downv:
    docker compose down -v
