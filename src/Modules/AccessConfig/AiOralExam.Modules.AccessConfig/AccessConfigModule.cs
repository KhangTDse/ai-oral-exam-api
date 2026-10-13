using AiOralExam.Modules.AccessConfig.Application;
using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.Modules.AccessConfig.Application.ExamSessions;
using AiOralExam.Modules.AccessConfig.Contracts;
using AiOralExam.Modules.AccessConfig.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AiOralExam.Modules.AccessConfig;

public static class AccessConfigModule
{
    public static IServiceCollection AddAccessConfigModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccessConfigDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IExamSessionQueries, ExamSessionQueries>();

        // Public contract for other modules
        services.AddScoped<IExamConfigurationApi, ExamConfigurationApi>();
        return services;
    }
}
