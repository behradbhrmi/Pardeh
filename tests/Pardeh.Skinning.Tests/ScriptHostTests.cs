using Pardeh.Skinning.Runtime.Scripting;

namespace Pardeh.Skinning.Tests;

public class ScriptHostTests
{
    private sealed class Sample : ScriptableObject
    {
        public double Volume = 50;
        public bool Down;
        public int Plays;

        public Sample()
        {
            Property("volume", () => Volume, v => Volume = Convert.ToDouble(v));
            Property("down", () => Down, v => Down = v is true);
            Method("play", () => Plays++);
            Method("add", args => Convert.ToDouble(args[0]) + Convert.ToDouble(args[1]));
        }
    }

    private readonly List<string> _log = [];

    private ScriptHost CreateHost(Sample sample)
    {
        var host = new ScriptHost(_log.Add);
        host.SetGlobal("player", sample);
        return host;
    }

    [Fact]
    public void Members_are_case_insensitive()
    {
        var sample = new Sample();
        var host = CreateHost(sample);

        Assert.Equal(50.0, host.Evaluate("player.VOLUME"));
        host.Execute("player.Volume = 80; player.PLAY(); player.play();", "test");

        Assert.Equal(80, sample.Volume);
        Assert.Equal(2, sample.Plays);
        Assert.Equal(7.0, host.Evaluate("player.Add(3, 4)"));
        Assert.Empty(_log);
    }

    [Fact]
    public void Handlers_see_element_members_and_event_arguments()
    {
        var sample = new Sample();
        var host = CreateHost(sample);
        host.Execute("var seen = 0; function OnState(s) { seen = s; }", "test");

        host.RunHandler(sample, "down = !down; OnState(NewState); volume = 10;", [("NewState", 3.0)]);

        Assert.True(sample.Down);
        Assert.Equal(10, sample.Volume);
        Assert.Equal(3.0, host.Evaluate("seen"));
        Assert.Empty(_log);
    }

    [Fact]
    public void Unknown_members_are_undefined_and_expandos_persist()
    {
        var sample = new Sample();
        var host = CreateHost(sample);

        Assert.Equal(true, host.Evaluate("player.nothing === undefined"));
        host.Execute("player.custom = 'x';", "test");
        Assert.Equal("x", host.Evaluate("player.Custom"));
    }

    [Fact]
    public void Script_errors_are_logged_not_thrown()
    {
        var host = CreateHost(new Sample());
        host.Execute("this is not javascript", "bad.js");
        Assert.Single(_log);
    }
}
