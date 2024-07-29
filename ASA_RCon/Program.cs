// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using System;
using System.Net.Sockets;


IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile(File.Exists("appsettings live.json")? "appsettings live.json" : "appsettings.json", true)
    .AddCommandLine(args)
    .Build();

// Get values from the config given their key and their target type.

Settings? settings = null;

settings = config.GetSection("Settings").Get<Settings>();


Socket rconSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

const int RCON_PID = 0xBADC0DE;

IAsyncResult? receiveResult = null;
var receiveBuffer = new byte[4096];
var authenticated = new ManualResetEvent(false);
var commandsResponsesReceived = new ManualResetEvent(false);
var ExpectingKeepAliveResponse = false;
var commandsCount = 0;
if (settings != null)
{
    bool running = true;
    Console.WriteLine($"Attempting to connect to {settings?.Host} on port {settings?.Port}.");
    
    try
    {
        rconSocket.Connect((string)(settings?.Host ?? "localhost"), (int)(settings?.Port ?? 27020));
    }
    catch(SocketException socketException) 
    {
        Console.WriteLine($"SocketException: {socketException.Message}");
        System.Environment.Exit(-1);
    }
    receiveResult = rconSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ReceiveCallback, null);
    Console.WriteLine("Connected to RCon Server.");
    var packet = BuildPacket(RCON_COMMAND_CODES.RCON_AUTHENTICATE, settings?.Password ?? "");
    var buffer = SerializePacket(packet);
    rconSocket.Send(buffer);
    Console.WriteLine("Authentication request sent.");
    
    authenticated.WaitOne();

    if(settings?.Command != null) 
    {
        var commandlist = settings.Command.Split(';');

        ExecuteCommands(commandlist);
        commandsResponsesReceived.WaitOne();
        running = false;
    }

    if(settings?.BatchPath != null) 
    {
        if(!File.Exists(settings.BatchPath)) 
        {
            Console.WriteLine("Batch text file not found.");
        }
        else
        {
            var lines = File.ReadLines(settings.BatchPath);
            commandsResponsesReceived.Reset();
            
            ExecuteCommands(lines);

            commandsResponsesReceived.WaitOne();
        }
        running = false;
    }
    
    while (running)
    {
        var task = Console.In.ReadLineAsync();
        task.Wait();
        var command = task.Result;
        if(command != null && command.Equals("exit", StringComparison.InvariantCultureIgnoreCase))
        {
            running = false;
            
        }
        else if(!string.IsNullOrEmpty(command))
        {
            packet = BuildPacket(RCON_COMMAND_CODES.RCON_EXEC_COMMAND, command);
            buffer = SerializePacket(packet);
            rconSocket.Send(buffer);
        } 
        else
        {
            packet = BuildPacket(RCON_COMMAND_CODES.RCON_EXEC_COMMAND, "ListPlayers");
            buffer = SerializePacket(packet);
            rconSocket.Send(buffer);
        }
    }

    Console.WriteLine("Goodbye.");
}
else
{
    Console.WriteLine("Settings not found.");
}

void ExecuteCommands(IEnumerable<string> commands) 
{
    foreach(var command in commands) 
    {
        if(command.Trim().StartsWith("WAIT", StringComparison.InvariantCultureIgnoreCase))
        {
            System.Threading.Thread.Sleep(100);
        }
        else
        {
            var packet = BuildPacket(RCON_COMMAND_CODES.RCON_EXEC_COMMAND, command);
            var buffer = SerializePacket(packet);
            rconSocket.Send(buffer);
            commandsCount++;
        }
    }
}

void ReceiveCallback(object state)
{
    if(receiveResult != null) {
        var length = rconSocket.EndReceive(receiveResult);

        if( length > 0 )
        {
            var parsed = 0;
            while (parsed < length)
            {
                var packet = new RConPacket();
                packet.size = BitConverter.ToInt32(receiveBuffer, parsed);
                packet.id = BitConverter.ToInt32(receiveBuffer, parsed + 3);
                packet.command_code = BitConverter.ToInt32(receiveBuffer, parsed + 7);
                packet.command = System.Text.Encoding.ASCII.GetString(receiveBuffer, parsed + 12, Math.Max(0, Math.Min(length - 14, packet.size - 8)));
                parsed += Math.Min(length, packet.size + 4);
                if (packet.command_code != 0)
                {
                    if (packet.command_code == 523)
                    {
                        if (packet.id == -1)
                        {
                            Console.WriteLine("Authentication error");
                            rconSocket.Shutdown(SocketShutdown.Both);
                            System.Environment.Exit(-1);
                        }
                        else
                        {
                            Console.WriteLine("Authenticated.");
                            Console.Write("> ");
                        }
                        var _timer = new Timer(KeepAliveCallback, null, 20000, 20000);
                        authenticated.Set();
                    } 
                    else if(packet.command_code == 767)
                    {
                        Console.WriteLine("Authentication failed.");
                        System.Environment.Exit(0);
                    }
                    else if (ExpectingKeepAliveResponse && packet.command_code == 11 && packet.command.StartsWith("Server received, But no response!!"))
                    {
                        ExpectingKeepAliveResponse = false;
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now} ::: SIZE: {packet.size} ID: {packet.id} CODE: {packet.command_code} TEXT: {packet.command.Trim()}");
                        Console.Write("> ");
                        if(commandsCount > 0 && --commandsCount == 0) {
                            commandsResponsesReceived.Set();
                        }
                    }
                }                
            }
            receiveResult = rconSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ReceiveCallback, null);
        }
        else
        {
            Console.WriteLine("Received 0 bytes, exiting...");
            try
            {
                rconSocket.Shutdown(SocketShutdown.Both);
            } 
            catch
            {

            }
            System.Environment.Exit(0);
        }
    }
}

void KeepAliveCallback(object? state)
{
    ExpectingKeepAliveResponse = true;
    var packet = BuildPacket(RCON_COMMAND_CODES.RCON_RESPONSEVALUE, "KeepAlive");
    var buffer = SerializePacket(packet);
    rconSocket.Send(buffer);
}

RConPacket BuildPacket(RCON_COMMAND_CODES CommandCode, string CommandText)
{
    return new RConPacket()
    {
        id = RCON_PID,
        command_code = (int) CommandCode,
        command = CommandText,
        size = sizeof(int) + sizeof(int) + CommandText.Length + 2
    };
}

byte[] SerializePacket(RConPacket packet)
{
    using (var stream = new MemoryStream())
    {
        stream.Write(BitConverter.GetBytes(packet.size));
        stream.Write(BitConverter.GetBytes(packet.id));
        stream.Write(BitConverter.GetBytes(packet.command_code));
        stream.Write(System.Text.Encoding.ASCII.GetBytes(packet.command));
        stream.WriteByte(0);
        stream.WriteByte(0);
        return stream.ToArray();
    }
}

public sealed class Settings
{
    public required string Host { get; set; } = "localhost";
    public required int Port { get; set; } = 27020;
    public required string Password { get; set; } = "password";

    public string? Command {get;set;}

    public string? BatchPath {get;set;}
}

public enum RCON_COMMAND_CODES
{
    RCON_EXEC_COMMAND = 2,
    RCON_AUTHENTICATE = 3,
    RCON_RESPONSEVALUE = 0,
    RCON_AUTH_RESPONSE = 2,
    
}

public class RConPacket
{
    public int size {get;set;}
    public int id {get;set;}
    public int command_code {get;set;}
    public string command {get;set;} = string.Empty;
}