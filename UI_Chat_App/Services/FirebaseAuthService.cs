using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ChatApp.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ChatApp.Services
{
    public class FirebaseAuthService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public FirebaseAuthService()
        {
            _apiKey = ConfigHelper.GetFirebaseApiKey();
            _httpClient = new HttpClient();
        }

        public async Task<(string IdToken, string RefreshToken, string Uid)> SignInWithEmailAndPasswordAsync(string email, string password)
        {
            var requestData = new
            {
                email,
                password,
                returnSecureToken = true
            };

            var response = await _httpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}",
                new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorObj = JsonConvert.DeserializeObject<JObject>(errorContent);
                throw new Exception(errorObj["error"]?["message"]?.ToString() ?? "Sign-in failed");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<JObject>(responseContent);
            return (
                responseObj["idToken"]?.ToString(),
                responseObj["refreshToken"]?.ToString(),
                responseObj["localId"]?.ToString()
            );
        }

        public async Task<(string IdToken, string RefreshToken, string Uid)> SignUpWithEmailAndPasswordAsync(string email, string password)
        {
            var requestData = new
            {
                email,
                password,
                returnSecureToken = true
            };

            var response = await _httpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}",
                new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorObj = JsonConvert.DeserializeObject<JObject>(errorContent);
                throw new Exception(errorObj["error"]?["message"]?.ToString() ?? "Sign-up failed");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<JObject>(responseContent);
            return (
                responseObj["idToken"]?.ToString(),
                responseObj["refreshToken"]?.ToString(),
                responseObj["localId"]?.ToString()
            );
        }

        public async Task SendEmailVerificationAsync(string idToken)
        {
            var requestData = new
            {
                requestType = "VERIFY_EMAIL",
                idToken
            };

            var response = await _httpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={_apiKey}",
                new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorObj = JsonConvert.DeserializeObject<JObject>(errorContent);
                throw new Exception(errorObj["error"]?["message"]?.ToString() ?? "Failed to send verification email");
            }
        }

        public async Task SendPasswordResetEmailAsync(string email)
        {
            var requestData = new
            {
                requestType = "PASSWORD_RESET",
                email
            };

            var response = await _httpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={_apiKey}",
                new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorObj = JsonConvert.DeserializeObject<JObject>(errorContent);
                throw new Exception(errorObj["error"]?["message"]?.ToString() ?? "Failed to send password reset email");
            }
        }

        public async Task<bool> IsEmailVerifiedAsync(string idToken)
        {
            var requestData = new
            {
                idToken
            };

            var response = await _httpClient.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={_apiKey}",
                new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var errorObj = JsonConvert.DeserializeObject<JObject>(errorContent);
                throw new Exception(errorObj["error"]?["message"]?.ToString() ?? "Failed to check email verification");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<JObject>(responseContent);
            return responseObj["users"]?[0]?["emailVerified"]?.ToObject<bool>() ?? false;
        }

    }
}