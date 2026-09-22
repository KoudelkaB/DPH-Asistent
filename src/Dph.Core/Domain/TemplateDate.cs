namespace Dph.Core.Domain;

/// <summary>
/// Posun data zdanitelného plnění při kopírování řádků jako šablony do nového období.
/// </summary>
public static class TemplateDate
{
    /// <summary>
    /// Přenese DUZP do zadaného měsíce se zachováním dne. Pravidelné faktury mívají DUZP pořád
    /// na stejný den v měsíci, takže kopie má sedět na tentýž den.
    /// Den, který v cílovém měsíci není (31. → duben, 30. → únor), se zkrátí na poslední den.
    /// Za „poslední den měsíce“ se naopak nepovažuje 28. 2. ani 30. 4. – u pravidelné faktury
    /// na 28. nebo 30. je to konkrétní číslo a kopie má zůstat na něm.
    /// </summary>
    public static DateOnly ShiftToMonth(DateOnly source, int year, int month)
        => new(year, month, Math.Min(source.Day, DateTime.DaysInMonth(year, month)));
}
