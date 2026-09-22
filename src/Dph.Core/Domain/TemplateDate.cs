namespace Dph.Core.Domain;

/// <summary>
/// Posun data zdanitelného plnění při kopírování řádků jako šablony do nového období.
/// </summary>
public static class TemplateDate
{
    /// <summary>
    /// Přenese DUZP do zadaného měsíce se zachováním dne. Pravidelné faktury mívají DUZP pořád
    /// na stejný den v měsíci, takže kopie má sedět na tentýž den.
    /// Výjimka je poslední den měsíce: ten se drží na konci cílového měsíce (31. 1. → 28. 2.),
    /// protože „poslední den“ je záměr, ne konkrétní číslo. Den, který v cílovém měsíci není
    /// (30. → únor), se stejně tak zkrátí na poslední den.
    /// </summary>
    public static DateOnly ShiftToMonth(DateOnly source, int year, int month)
    {
        var daysInTarget = DateTime.DaysInMonth(year, month);
        var isEndOfMonth = source.Day == DateTime.DaysInMonth(source.Year, source.Month);
        var day = isEndOfMonth ? daysInTarget : Math.Min(source.Day, daysInTarget);
        return new DateOnly(year, month, day);
    }
}
