# BeatLeader Server

Server for the BeatLeader website and Beat Saber mod.

Currently deployed here: https://beatleader.azurewebsites.net/

I don't know what I'm doing, so there is can be very stupid mistakes!

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
3) `sudo docker run --cap-add SYS_PTRACE -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=SuperStrong!' -p 1433:1433 --name sqledge -d mcr.microsoft.com/azure-sql-edge`
4) `dotnet tool install --global dotnet-ef`
5) `dotnet ef migrations add InitialDb`
6) `dotnet ef database update`
7) `azurite -s -l azstorage -d azstorage\debug.log`
8) Open the .sln in Visual Studio and run the project.

The server should be accessible at https://localhost:7040/

### Release build

This project uses Azure to deploy. You will be needed to create SQL server and blob storage in order to deploy.

## API 

This API is in a very early stage and will certainly change.

// TODO: Move this to Swagger

### Authentication

```
GET /signin - will open web Login with Steam authentication
GET /signout - will delete Steam cookie.

// Only for the authenticated user
GET /user/id - Steam ID of the signed user.
```

### Replay posting

```
POST /replay - Post the score using .bsor file.
```

### Player

```
GET /player/{id} - Returns info for the player.
GET /player/{id}/scores?
```

### Playlists

```
GET /playlists - All shared playlists.
GET /playlist/id/{id} - Playlist with specified ID.

// Only for the authenticated user
GET /user/playlists - All the playlists for the user.
GET /user/playlist/id/{id} - Playlist with specified ID.
POST /user/playlist?shared={optional bool. Is this playlist shared for others} - Add the playlist. Playlist value is JSON string in body.
PATCH /user/playlist/id/{id}?shared={optional bool} - Update the playlist
DELETE /user/playlist/id/{id} - Delete playlist
```