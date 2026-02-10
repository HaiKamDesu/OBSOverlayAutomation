using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

internal class Program
{
    private static async Task Main()
    {
        var url = "ws://192.168.0.21:4455";
        var password = "8tduawTg9CLnz7d7";

        var obs = new OBSWebsocket();

        // Used to await the Connected event
        var connectedTcs = new TaskCompletionSource<bool>();

        obs.Connected += (_, _) =>
        {
            Console.WriteLine("OBS Connected!");
            connectedTcs.TrySetResult(true);
        };

        obs.Disconnected += (_, _) =>
        {
            Console.WriteLine("OBS Disconnected!");
        };

        try
        {
            // Start connection (non-awaitable)
            obs.ConnectAsync(url, password);

            // Properly wait until OBS confirms connection
            await connectedTcs.Task;

            Console.WriteLine($"IsConnected = {obs.IsConnected}");

            // Test updates
            SetText(obs, "P1 Player Name", "Franco");

            Console.WriteLine("Updated text sources. Press Enter to quit.");
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error:");
            Console.WriteLine(ex);
            Console.ReadLine();
        }
        finally
        {
            if (obs.IsConnected)
                obs.Disconnect();
        }
    }

    private static void SetText(OBSWebsocket obs, string inputName, string text)
    {
        var settings = new JObject
        {
            ["text"] = text
        };

        obs.SetInputSettings(inputName, settings, true);
    }
}
