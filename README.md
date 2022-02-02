# BeatLeader Server

Server for the website and game mod.

Currently deployed here: https://beatleader.azurewebsites.net/

I don't know what I'm doing, so there is can be very stupid mistakes!

## API 

This API is in very early stage and will certanly change.

// TODO: Move this to Swagger

### Authentication

GET /signin - will open web Login with Steam authentication
GET /signout - will delete Steam cookie.

GET /user/id - Steam ID of the signed user.

### Playlists

GET /playlists - All shared playlists.
GET /playlist?id={int} - Playlist with specified ID.

GET /user/playlists - All the playlists for user.
POST /user/playlist?shared={optional bool. Is this playlist shared for others} - Add the playlist. Playlist value is JSON string in body.
PATCH /user/playlist?id={int}&shared={optional bool} - Update the playlist
DELETE /user/playlist?id={int} - Delete playlist
GET /user/playlist?id={int} - Playlist with specified ID.
