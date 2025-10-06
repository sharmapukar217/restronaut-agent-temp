using System.Threading.Tasks;

namespace RestronautService.Middlewares;
public interface IAuthTokenStore
{
    Task<string> GetAuthTokenAsync();
}