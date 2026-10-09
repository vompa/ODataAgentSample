namespace ODataAgentSample.Demo;

public sealed record DemoLimits(
    int HourLimit,
    int HourRemaining,
    int SessionHourLimit,
    int SessionHourRemaining,
    int DayLimit,
    int DayRemaining);

public readonly record struct QuotaResult(bool Allowed, TimeSpan RetryAfter, DemoLimits Limits);

/// <summary>
/// Kontingent im Speicher: gleitendes Fenster, 100 Abfragen pro Stunde fuer alle, je Sitzung 15 pro Stunde, 400 pro Tag.
/// Ein Neustart der App setzt die Zaehler zurueck; die harte Kostengrenze ist das Ausgabenlimit bei Anthropic.
/// Die drei Fenster werden unter einer Sperre gemeinsam geprueft, parallele Anfragen koennen das Limit nicht ueberschreiten.
/// </summary>
public sealed class DemoQuota(DemoOptions options, TimeProvider clock)
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private const int MaxTrackedSessions = 10_000;

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _hour = new();
    private readonly Queue<DateTimeOffset> _day = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _sessions = new(StringComparer.Ordinal);

    public QuotaResult TryAcquire(string sessionId)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var session = Prune(now, sessionId);

            var retryAfter = TimeSpan.Zero;
            if (_hour.Count >= options.HourLimit)
                retryAfter = Max(retryAfter, _hour.Peek() + Hour - now);
            if (session.Count >= options.SessionHourLimit)
                retryAfter = Max(retryAfter, session.Peek() + Hour - now);
            if (_day.Count >= options.DayLimit)
                retryAfter = Max(retryAfter, _day.Peek() + Day - now);

            if (retryAfter > TimeSpan.Zero)
                return new QuotaResult(false, retryAfter, Snapshot(session));

            _hour.Enqueue(now);
            _day.Enqueue(now);
            session.Enqueue(now);
            return new QuotaResult(true, TimeSpan.Zero, Snapshot(session));
        }
    }

    public DemoLimits Limits(string sessionId)
    {
        lock (_gate)
        {
            var session = Prune(clock.GetUtcNow(), sessionId);
            return Snapshot(session);
        }
    }

    private Queue<DateTimeOffset> Prune(DateTimeOffset now, string sessionId)
    {
        Drop(_hour, now - Hour);
        Drop(_day, now - Day);

        if (_sessions.Count > MaxTrackedSessions)
        {
            foreach (var key in _sessions.Where(s => s.Value.Count == 0).Select(s => s.Key).ToList())
                _sessions.Remove(key);
        }

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            session = new Queue<DateTimeOffset>();
            _sessions[sessionId] = session;
        }

        Drop(session, now - Hour);
        return session;
    }

    private DemoLimits Snapshot(Queue<DateTimeOffset> session) => new(
        options.HourLimit,
        Math.Max(0, options.HourLimit - _hour.Count),
        options.SessionHourLimit,
        Math.Max(0, options.SessionHourLimit - session.Count),
        options.DayLimit,
        Math.Max(0, options.DayLimit - _day.Count));

    private static void Drop(Queue<DateTimeOffset> queue, DateTimeOffset cutoff)
    {
        while (queue.Count > 0 && queue.Peek() <= cutoff)
            queue.Dequeue();
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
