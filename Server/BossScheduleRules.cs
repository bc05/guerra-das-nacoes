using System.Globalization;

namespace GuerraDasNacoes.Server;

// Calendar values are interpreted in the schedule's IANA zone; combat uses UTC seconds.
internal static class BossScheduleRules
{
    internal readonly record struct Occurrence(string Id, double StartsAt, double EndsAt);
    public static void Validate(EconomyConfig config, List<string> errors)
    {
        EconomyMonsterConfig[] monsters;
        try { monsters = MonsterCatalog.Resolve(config); }
        catch (ArgumentException e) { errors.Add(e.Message); return; }
        var schedules = config.bossSchedules ?? Array.Empty<EconomyBossSchedule>();
        if (schedules.Length > 30 || schedules.Any(s=>s==null)) { errors.Add("Agendas de chefão inválidas."); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in schedules)
        {
            if (string.IsNullOrWhiteSpace(s.id) || s.id.Length>100 || !ids.Add(s.id) || !profiles.Add(s.monsterId??"")) errors.Add("Identidade de agenda/chefão ausente ou repetida.");
            var monster = monsters.FirstOrDefault(m=>m.id==s.monsterId);
            if (monster==null || !monster.isBoss || monster.scheduleId!=s.id) errors.Add("Agenda deve apontar para seu perfil de chefão: "+s.id);
            if (!TimeOnly.TryParseExact(s.startTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) || s.daysOfWeek==null || s.daysOfWeek.Length<1 || s.daysOfWeek.Length>7 || s.daysOfWeek.Any(d=>d<0||d>6) || s.daysOfWeek.Distinct().Count()!=s.daysOfWeek.Length || s.durationMinutes<5 || s.durationMinutes>720) errors.Add("Use dias 0–6, horário HH:mm e duração de 5–720 minutos: "+s.id);
            try { _=TimeZoneInfo.FindSystemTimeZoneById(s.timeZone??""); } catch(Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { errors.Add("Fuso horário desconhecido: "+s.id); }
        }
        foreach(var m in monsters)
            if(!m.isBoss&&!string.IsNullOrEmpty(m.scheduleId)) errors.Add("Monstro normal não pode usar agenda de chefão: "+m.id);
            else if(m.isBoss && (!schedules.Any(s=>s?.id==m.scheduleId&&s.monsterId==m.id))) errors.Add("Chefão exige agenda, inclusive quando desativada: "+m.id);
    }
    public static IEnumerable<Occurrence> Occurrences(EconomyBossSchedule schedule, double now, bool upcoming)
    {
        if(!schedule.enabled)yield break;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(schedule.timeZone);
        DateTime local=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(now*1000)),zone).Date;
        var time=TimeOnly.ParseExact(schedule.startTime,"HH:mm",CultureInfo.InvariantCulture);
        for(int day=upcoming?-1:-1;day<=(upcoming?8:0);day++)
        {
            DateTime date=DateTime.SpecifyKind(local.AddDays(day).Add(time.ToTimeSpan()),DateTimeKind.Unspecified);
            if(!schedule.daysOfWeek.Contains((int)date.DayOfWeek)||zone.IsInvalidTime(date))continue;
            // An ambiguous wall clock time is one occurrence, at its first UTC instant.
            TimeSpan offset=zone.IsAmbiguousTime(date)?zone.GetAmbiguousTimeOffsets(date).Max():zone.GetUtcOffset(date);
            double start=new DateTimeOffset(date,offset).ToUnixTimeSeconds();
            yield return new Occurrence(schedule.id+":"+((long)start).ToString(CultureInfo.InvariantCulture),start,start+schedule.durationMinutes*60);
        }
    }
}
