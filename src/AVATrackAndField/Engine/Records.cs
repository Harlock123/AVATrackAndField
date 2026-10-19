using System.Globalization;

namespace AVATrackAndField.Engine;

/// <summary>
/// The world records, one for each event, kept beside the high scores. They start at the marks of 1983 and fall
/// to whoever beats them on this machine.
/// </summary>
public sealed class Records
{
    /// <summary>The marks of 1983: 9.93, 8.90 m, 99.72 m, 12.93, 84.14 m and 2.38 m.</summary>
    public static readonly double[] Starting = [9.93, 8.90, 99.72, 12.93, 84.14, 2.38];

    private readonly string _folder;
    private readonly double[] _marks = (double[])Starting.Clone();
    private string FilePath => Path.Combine(_folder, "records.txt");

    private Records(string folder) => _folder = folder;

    public double this[Ev e] => _marks[(int)e];

    public static Records Load(string folder)
    {
        var r = new Records(folder);
        try
        {
            if (File.Exists(r.FilePath))
                foreach (var line in File.ReadAllLines(r.FilePath))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2 && Enum.TryParse<Ev>(parts[0], out var e) && Enum.IsDefined(e)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mark) && mark > 0)
                        r._marks[(int)e] = mark;
                }
        }
        catch
        {
            // Unreadable records start again from 1983.
        }
        return r;
    }

    public bool Beaten(Ev e, double mark) => Sim.Better(e, mark, this[e]);

    /// <summary>A new record, if the mark beats the old one.</summary>
    public bool Set(Ev e, double mark)
    {
        if (!Beaten(e, mark)) return false;
        _marks[(int)e] = mark;
        return true;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            string temp = FilePath + ".tmp";
            File.WriteAllLines(temp, Enum.GetValues<Ev>().Select(e => $"{e}|{this[e].ToString("0.00", CultureInfo.InvariantCulture)}"));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // A read-only home folder just means the records aren't remembered.
        }
    }
}
