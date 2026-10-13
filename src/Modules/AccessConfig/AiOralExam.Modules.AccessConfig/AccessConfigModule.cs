using AiOralExam.Modules.AccessConfig.Application;
using AiOralExam.Modules.AccessConfig.Application.AIServices;
using AiOralExam.Modules.AccessConfig.Application.AuditLogs;
using AiOralExam.Modules.AccessConfig.Application.Auth;
using AiOralExam.Modules.AccessConfig.Application.Courses;
using AiOralExam.Modules.AccessConfig.Application.ExamSessions;
using AiOralExam.Modules.AccessConfig.Application.LecturerAssignments;
using AiOralExam.Modules.AccessConfig.Application.Participants;
using AiOralExam.Modules.AccessConfig.Application.Questions;
using AiOralExam.Modules.AccessConfig.Application.Rubrics;
using AiOralExam.Modules.AccessConfig.Application.Users;
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
        services.AddScoped<IExamSessionCommands, ExamSessionCommands>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IQuestionService, QuestionService>();
        services.AddScoped<IRubricService, RubricService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IParticipantService, ParticipantService>();
        services.AddScoped<ILecturerAssignmentService, LecturerAssignmentService>();
        services.AddScoped<IAiConfigService, AiConfigService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddDataProtection();

        // Public contract for other modules
        services.AddScoped<IExamConfigurationApi, ExamConfigurationApi>();
        return services;
    }
}
