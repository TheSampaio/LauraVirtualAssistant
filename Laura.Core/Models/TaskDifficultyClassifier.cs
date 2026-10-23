using Laura.Core.Conversation;
using Laura.Core.Text;

namespace Laura.Core.Models;

/// <summary>Estimates a task floor from native progress and explicit technical requirements without spending inference tokens.</summary>
public static class TaskDifficultyClassifier
{
    /// <summary>Classifies real requirements rather than message length. Args: prompt: Owner intent; demand: Native observed task demand; image: Explicit image requirement. Returns: Recommended difficulty floor.</summary>
    public static TaskDifficulty Classify(string prompt, CognitiveTaskDemand demand, bool image = false)
    {
        string text = TextNormalizer.Normalize(prompt);
        string[] maximum = ["modo max", "max mode", "analise exaustiva", "análise exaustiva", "revisao critica completa", "revisão crítica completa"];
        if (maximum.Any(term => TextNormalizer.ContainsPhrase(text, term))) { return TaskDifficulty.Max; }
        if (demand is CognitiveTaskDemand.Analysis or CognitiveTaskDemand.Synthesis) { return TaskDifficulty.Advanced; }
        string[] advanced = ["architecture", "arquitetura", "root cause", "causa raiz", "race condition", "concurrency", "concorrencia", "security audit", "auditoria de seguranca", "migracao", "migration"];
        if (advanced.Any(term => TextNormalizer.ContainsPhrase(text, term))) { return TaskDifficulty.Advanced; }
        string[] standard = ["landing page", "debug", "depure", "refactor", "refatore", "compare", "compare os", "estude", "study", "implemente", "implement"];
        return image || demand == CognitiveTaskDemand.Planning || standard.Any(term => TextNormalizer.ContainsPhrase(text, term))
            ? TaskDifficulty.Standard : TaskDifficulty.Light;
    }
}
