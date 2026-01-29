using LittleHelperAI.Shared.Models;
using System.Net.Http.Json;

namespace LittleHelperAI.Dashboard.Services
{
    public class PlanService
    {
        private readonly HttpClient _httpClient;

        public PlanService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<StripePlan>> GetPlansAsync()
        {
            var response = await _httpClient.GetFromJsonAsync<List<StripePlan>>("/api/plans");
            return response ?? new List<StripePlan>();
        }

        public async Task<string> StartPurchaseAsync(int planId)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/plans/buy", new { PlanId = planId });

            if (response.IsSuccessStatusCode)
            {
                var checkoutUrl = await response.Content.ReadAsStringAsync();
                return checkoutUrl;
            }

            throw new Exception("Failed to start purchase.");
        }

        public async Task<List<UserPlan>> GetUserPlansAsync()
        {
            var response = await _httpClient.GetFromJsonAsync<List<UserPlan>>("/api/plans/userplans");
            return response ?? new List<UserPlan>();
        }
    }
}
