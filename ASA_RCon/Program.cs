// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using System;
using System.Net.Sockets;


IConfigurationRoot config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", true)
    .AddCommandLine(args)
    .Build();

// Get values from the config given their key and their target type.

Settings? settings = null;

settings = config.GetSection("Settings").Get<Settings>();


Socket rconSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

const int RCON_PID = 0xBADC0DE;

IAsyncResult receiveResult = null;
var receiveBuffer = new byte[4096];
var authenticated = false;
var ExpectingKeepAliveResponse = false;
if (settings != null)
{
    bool running = true;
    Console.WriteLine($"Attempting to connect to {settings?.RConHost} on port {settings?.RConPort}.");
    
    rconSocket.Connect((string)(settings?.RConHost ?? "localhost"), (int)(settings?.RConPort ?? 27020));
    
    receiveResult = rconSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ReceiveCallback, null);
    Console.WriteLine("Connected to RCon Server.");
    var packet = BuildPacket(RCON_COMMAND_CODES.RCON_AUTHENTICATE, settings?.RConPassword ?? "");
    var buffer = SerializePacket(packet);
    rconSocket.Send(buffer);
    Console.WriteLine("Authentication request sent.");
    
    while (running)
    {
        var task = Console.In.ReadLineAsync();
        task.Wait();
        var command = task.Result;
        if(command != null && command.Equals("exit", StringComparison.InvariantCultureIgnoreCase))
        {
            running = false;
            Console.WriteLine("Goodbye.");
        }
        else if(command.Length != 0)
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
}
else
{
    Console.WriteLine("Settings not found.");
}

void ReceiveCallback(object state)
{
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
                    }
                    else
                    {
                        Console.WriteLine("Authenticated.");
                        Console.Write("> ");
                    }
                    var _timer = new Timer(KeepAliveCallback, null, 20000, 20000);
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
                }
            }
            //else
            //{
            //packet = BuildPacket(RCON_COMMAND_CODES.RCON_EXEC_COMMAND, "ListPlayers");
            //var buffer = SerializePacket(packet);
            //rconSocket.Send(buffer);
            //}
            
        }
        receiveResult = rconSocket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ReceiveCallback, null);
    }
    else
    {
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
    public required string RConHost { get; set; } = "localhost";
    public required int RConPort { get; set; } = 27020;
    public required string RConPassword { get; set; } = "password";
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
    public int size;
    public int id;
    public int command_code;
    public string command;
}