# BeatLeader Server

Server for the BeatLeader website and Beat Saber mod.

Deployed here: https://api.beatleader.xyz/

I don't know what I'm doing, so there can be very stupid mistakes!

## Description

The main data source for the server are replays. It is operating by the simple rule - no replay, no score.

## Development (Windows or macOS)

### Local build

To start this thing you need to install several tools:

- Visual Studio 2022
- Docker

For testing use Postman.

After everything is installed:

1) Change working directory to project.
2) `dotnet tool install --global dotnet-ef`
3) `dotnet tool restore`
4) `dotnet ef database update --context AppContext`
5) `sudo docker run --cap-add SYS_PTRACE -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=SuperStrong!' -p 1433:1433 --name sqledge -d mcr.microsoft.com/azure-sql-edge` (On Windows use " instead of ')
6) Open the .sln in Visual Studio and run the server with "IIS Express".

The server should be accessible at https://localhost:44313/

## API 

There is an automatically generated Swagger documentation available at the following link:

http://api.beatleader.xyz/swagger/index.html

---

### Using Oauth2.0 on BeatLeader

BeatLeader Oauth2.0 supports only Authorization Code Grant, it will work only if you have server side as well.
First off you need to dm [nsgolova](https://discordapp.com/users/698212038106677259) on Discord and send him the service information, the icon for the service and the callback endpoint for your website and local development (for example: `https://api.example-site.com/beatleader-login` and `http://localhost:3000/beatleader-login`). You will get a client id and client secret.

For this example, we'll use these variables:
- Client ID = exampleID
- Client Secret = exampleSecret
- Callback URL = http://localhost:3000/beatleader-login

Disclaimer! It will be much easier to use a library for that, for example with AspNet.Security.OpenId. There is a C# example [here](/Auth/Beatleader/BeatLeaderAuthenticationDefaults.cs)

Now you need to construct a URL for the oauth2. If we use the variables above, the URL should look something like this: `https://api.beatleader.xyz/oauth2/authorize?client_id=exampleID&response_type=code&scope=profile&redirect_uri=http://localhost:3000/beatleader-login`.

**Note:** If you want the server issue also refresh token, you need to add ``offline_access`` to ``scope`` parameter of ``oauth2/authorize`` request. Scopes in OAuth2 Specification are separated by a space, so the parameter should be ``profile%20offline_access`` (%20 is a space encoded in the url).

When the person authorizes access to their account information, BeatLeader will redirect the user to your callback URL with a `code` and `iss` query parameter.

Let's say that the code we got back is `exampleCode`.

Example return link: `http://localhost:3000/beatleader-login?code=exampleCode&iss=https%3A%2F%2Fapi.beatleader.xyz%2F`.

Now you need to get a token to make a request to identify a user. So now, on your server, you need to send a POST request to the following url: `https://api.beatleader.xyz/oauth2/token`. The headers should contain Content-Type of "application/x-www-form-urlencoded". And the body of the request needs to contain the following string: `grant_type=authorization_code&client_id=exampleID&client_secret=exampleSecret&code=exampleCode&redirect_uri=http://localhost:3000/beatleader-login`.

Simplified:
```
grant_type = authorization_code
client_id = exampleID
client_secret = exampleSecret
code = exampleCode
reidrect_uri = http://localhost:3000/beatleader-login
```

If the request was successful, you should get a JSON response containing the access token in the `access_token` field. If your scopes also contained ``offline_access`` you will also get a refresh token in the ``refresh_token`` field.

```json
{
    "access_token": "eyJ...",
    "token_type": "Bearer",
    "expires_in": 3600,
    "scope": "profile offline_access",
    "refresh_token": "eyJ..."
}
```

**THE ACCESS TOKEN IS VALID FOR 3600 seconds and the REFRESH TOKEN IS VALID FOR 14 days**

Let's say our access_token is `exampleToken`.

Now, to identify the user, we have to send a GET request to `https://api.beatleader.xyz/oauth2/identity` with a header "Authorization". The content of the Authorization header should be `Bearer exampleToken`.

If everything was done correctly you should have gotten a JSON response with the user's id and name.

**Note:** the following flow is only available if you also specified ``offline_access`` in ``scope`` parameter during authorization.

When the ``access_token`` expires you must refresh it. To do so, you must send a POST request to the endpoint ``https://api.beatleader.xyz/oauth2/token``. The headers should contain Content-Type of "application/x-www-form-urlencoded". And the body of the request needs to contain the following string: `grant_type=refresh_token&client_id=exampleID&client_secret=exampleSecret&refresh_token=OLD_REFRESH_TOKEN`.

If the request was successful, you should get a JSON response containing the **new access token** in the `access_token` field and **new refresh token** in the ``refresh_token`` field. The old tokens will be invalidated, so from then on you must use new ones.


