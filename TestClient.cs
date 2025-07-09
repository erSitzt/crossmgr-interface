using System.Net.Sockets;
using System.Text;

namespace TestClient;

class Program
{
  static async Task Main(string[] args)
  {
    string server = "localhost";
    int port = 53135;

    if (args.Length >= 1)
      server = args[0];
    if (args.Length >= 2)
      int.TryParse(args[1], out port);

    Console.WriteLine($"Connecting to {server}:{port}");
    Console.WriteLine("Press 'q' to quit, 'g' to send GT request, 's' to send S0000, 'd' to send DA message");

    try
    {
      using var client = new TcpClient();
      await client.ConnectAsync(server, port);

      var stream = client.GetStream();

      // Send initial identification
      await SendMessage(stream, "N0000TESTCLIENT-12345");

      var readTask = Task.Run(() => ReadMessages(stream));

      while (true)
      {
        var key = Console.ReadKey(true);

        switch (key.KeyChar)
        {
          case 'q':
          case 'Q':
            return;

          case 'g':
          case 'G':
            await SendMessage(stream, "GT");
            break;

          case 's':
          case 'S':
            await SendMessage(stream, "S0000");
            break;

          case 'd':
          case 'D':
            // Send a sample tag read
            var now = DateTime.Now;
            var count = Random.Shared.Next(0, 65535);
            var tagId = Random.Shared.Next(10000000, 99999999);
            var message = $"DA{tagId} {now:HH:mm:ss.ffffff} 10  {count:X5}      C7 date={now:yyyyMMdd}";
            await SendMessage(stream, message);
            break;

          default:
            Console.WriteLine("Unknown command. Use 'g' for GT, 's' for S0000, 'd' for DA, 'q' to quit");
            break;
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error: {ex.Message}");
    }
  }

  static async Task SendMessage(NetworkStream stream, string message)
  {
    var bytes = Encoding.ASCII.GetBytes(message + "\r\n");
    await stream.WriteAsync(bytes, 0, bytes.Length);
    Console.WriteLine($"Sent: {message}");
  }

  static async Task ReadMessages(NetworkStream stream)
  {
    var buffer = new byte[1024];
    var messageBuilder = new StringBuilder();

    try
    {
      while (true)
      {
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        if (bytesRead == 0)
          break;

        string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        messageBuilder.Append(data);

        string messages = messageBuilder.ToString();
        string[] lines = messages.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (messages.EndsWith('\r') || messages.EndsWith('\n'))
        {
          messageBuilder.Clear();
          foreach (string line in lines)
          {
            if (!string.IsNullOrWhiteSpace(line))
            {
              Console.WriteLine($"Received: {line}");
            }
          }
        }
        else
        {
          messageBuilder.Clear();
          for (int i = 0; i < lines.Length - 1; i++)
          {
            Console.WriteLine($"Received: {lines[i]}");
          }
          messageBuilder.Append(lines[lines.Length - 1]);
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Read error: {ex.Message}");
    }
  }
}
