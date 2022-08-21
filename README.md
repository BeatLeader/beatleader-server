# BeatLeader Server

Server for the BeatLeader website and Beat Saber mod.

Currently deployed here: https://api.beatleader.xyz/

I don't know what I'm doing, so there can be very stupid mistakes!

## Description

The main data source for the server are replays. It is operating by the simple rule - no replay, no score.

## Development (Windows or macOS)

### Local build

To start this thing you need to install several tools:

- Visual Studio 2022
- Docker
- NodeJS

For testing use Postman.

After everything is installed:

1) Change working directory to project.
2) `npm install -g azurite`
3) `sudo docker run --cap-add SYS_PTRACE -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=SuperStrong!' -p 1433:1433 --name sqledge -d mcr.microsoft.com/azure-sql-edge` (On Windows use " instead of ')
4) `dotnet tool install --global dotnet-ef`
5) `dotnet tool restore`
6) `dotnet ef database update`
7) `azurite -s -l azstorage -d azstorage\debug.log`
8) Open the .sln in Visual Studio and run the project.

The server should be accessible at https://localhost:7040/

### Release build

This project uses Azure to deploy. You will be needed to create SQL server and blob storage in order to deploy.

## API 

There is an automatically generated Swagger documentation available at the following link:

http://api.beatleader.xyz/swagger/index.html
