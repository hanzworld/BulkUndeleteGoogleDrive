using System.Linq;
using System.Net.Http;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace BulkUndeleteGoogleDrive
{
    public class GoogleDriveService : DriveService
    {
        public GoogleDriveService(HttpClient client) : base(
            CreateBaseObject(
                client.DefaultRequestHeaders.GetValues(Constants.SecretsInHeadersHack.Id).First(),
                client.DefaultRequestHeaders.GetValues(Constants.SecretsInHeadersHack.Secret).First()
            )
        )
        {
        }

        private static Initializer CreateBaseObject(string id, string secret)
        {
            UserCredential credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets()
                {
                    ClientId = id,
                    ClientSecret = secret
                },
                new[] { Scope.Drive },
                "user",
                CancellationToken.None,
                new FileDataStore("Books.ListMyLibrary")).Result;

            return new Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "ResolveMultiLabelledEmails",
            };
        }
    }
}