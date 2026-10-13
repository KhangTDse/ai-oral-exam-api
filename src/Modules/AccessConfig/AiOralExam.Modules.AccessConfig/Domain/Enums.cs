namespace AiOralExam.Modules.AccessConfig.Domain;

// Enum member names MUST match the CHECK constraints in database/01_schema.sql
// (EF Core stores enums as strings).

public enum AiServiceType
{
    STT,
    TTS,
    LLM
}

public enum SpeechLanguage
{
    Vietnamese,
    English
}
