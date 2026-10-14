using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AiOralExam.Modules.AccessConfig.Application.LecturerAssignments;
using AiOralExam.Modules.AccessConfig.Domain;
using AiOralExam.Modules.AccessConfig.Infrastructure;
using AiOralExam.SharedKernel.Errors;
using AiOralExam.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace AiOralExam.Modules.AccessConfig.Application.Courses;
public interface ICourseService
{
    Task<IReadOnlyList<CourseDto>> GetAllAsync(CancellationToken ct);
    Task<CourseDto> GetAsync(Guid id, CancellationToken ct);
    Task<CourseDto> CreateAsync(CreateCourseRequest r, CancellationToken ct);
    Task<CourseDto> UpdateAsync(Guid id, UpdateCourseRequest r, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

internal sealed class CourseService(
    AccessConfigDbContext db,
    ICurrentUser me,
    ILecturerAssignmentService lecturerAssignments) : ICourseService
{
    public async Task<IReadOnlyList<CourseDto>> GetAllAsync(CancellationToken ct)
        => await db.Courses.AsNoTracking().OrderBy(x => x.Code)
        .Select(x => new CourseDto(x.Id, x.Code, x.Name, x.Lecturers.Count, x.ExamSessions.Count))
        .ToListAsync(ct);

    public async Task<CourseDto> GetAsync(Guid id, CancellationToken ct)
        => await db.Courses.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new CourseDto(x.Id, x.Code, x.Name, x.Lecturers.Count, x.ExamSessions.Count))
        .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("course_not_found", "Course not found.");

    public async Task<CourseDto> CreateAsync(CreateCourseRequest r, CancellationToken ct)
    {
        if (r.LecturerId == Guid.Empty)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["lecturerId"] = ["A lecturer ID is required."]
            });
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Courses.AnyAsync(x => x.Code == code, ct))
            throw new ConflictException("course_code_exists", "Course code already exists.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await lecturerAssignments.ValidateLecturerAsync(r.LecturerId, ct);
        var c = new Course { Id = Guid.NewGuid(), Code = code, Name = r.Name.Trim() };
        db.Courses.Add(c);
        await db.SaveChangesAsync(ct);
        await lecturerAssignments.AssignAsync(c.Id, r.LecturerId, ct);
        await Audit(db, me.Id, "Create", "Course", c.Id, ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(c.Id, ct);
    }

    public async Task<CourseDto> UpdateAsync(Guid id, UpdateCourseRequest r, CancellationToken ct)
    {
        var c = await db.Courses.FindAsync([id], ct) ??
            throw new NotFoundException("course_not_found", "Course not found.");
        c.Name = r.Name.Trim();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.SaveChangesAsync(ct);
        await Audit(db, me.Id, "Update", "Course", id, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Courses.FindAsync([id], ct) ??
            throw new NotFoundException("course_not_found", "Course not found.");
        if (await db.ExamSessions.AnyAsync(x => x.CourseId == id, ct))
            throw new ConflictException("course_has_exam_sessions", "This course already has exam sessions.");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        db.Courses.Remove(c);
        await db.SaveChangesAsync(ct);
        await Audit(db, me.Id, "Delete", "Course", id, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    internal static async Task Audit(AccessConfigDbContext db, Guid user, string action, string entity, Guid? id, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = user,
            Action = action,
            TargetEntity = entity,
            TargetId = id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
