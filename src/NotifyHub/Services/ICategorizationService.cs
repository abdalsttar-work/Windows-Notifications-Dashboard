using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface ICategorizationService
{
    Task<string> CategorizeAsync(string appName, string title, string body);
    NotificationPriority DeterminePriority(string appName, string title, string body, string category);
    Task ReloadRulesAsync();
}
