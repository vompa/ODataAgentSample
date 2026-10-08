namespace ODataAgentSample.Agent;

/// <summary>Fehler, die der Agent selbst beheben kann (unbekannter Code, ungueltiger Parameter). Die Meldung ist fuer ihn bestimmt.</summary>
public sealed class AgentInputException(string message) : Exception(message);
