using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExampleTestProject;

internal class Program
{
    private static HttpClient _httpClient;
    private static string _accessToken = string.Empty;
    private const string _clientId = "019adada-8f97-7bf5-8e46-2797e0c5f978";
    private const string _clientSecret = "secret";
    private const string _authUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    private const string _apiUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

    static Program()
    {
        // Создаем кастомный обработчик с обходом SSL проверки
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
            {
                // ВНИМАНИЕ: Это только для разработки!
                // В продакшене всегда проверяйте сертификаты

                // Для тестового окружения Сбера можно добавить исключение
                if (cert?.Issuer?.Contains("Sberbank") == true ||
                    cert?.Issuer?.Contains("SberDevices") == true)
                {
                    return true;
                }

                // Для локального тестирования полностью отключаем проверку
                return true; // ОПАСНО для продакшена!

                // Для более безопасного подхода:
                // return sslPolicyErrors == SslPolicyErrors.None;
            }
        };

        _httpClient = new HttpClient(handler);
    }

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== GigaChat Console Client ===");
        Console.WriteLine();

        try
        {
            // Авторизация
            Console.WriteLine("Авторизация...");
            await AuthenticateAsync();
            Console.WriteLine("Авторизация успешна!");
            Console.WriteLine();

            // Основной цикл чата
            await RunChatLoop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"Внутренняя ошибка: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine("\nНажмите любую клавишу для выхода...");
        Console.ReadKey();
    }

    private static async Task AuthenticateAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _authUrl);

        // Добавляем заголовки
        request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"))}");
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());
        request.Headers.Add("Accept", "application/json");

        // Тело запроса
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("scope", "GIGACHAT_API_PERS")
        });
        request.Content = content;

        // Отправка запроса
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Парсинг ответа
        var responseJson = await response.Content.ReadAsStringAsync();
        var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseJson);

        _accessToken = authResponse?.AccessToken ?? throw new Exception("Не удалось получить токен");
    }

    private static async Task RunChatLoop()
    {
        Console.WriteLine("Чат с GigaChat начат. Введите 'exit' для выхода.");
        Console.WriteLine("Введите 'clear' для очистки истории.");
        Console.WriteLine();

        var messages = new List<Message>
        {
            new Message { Role = "system", Content = "Ты - полезный ассистент" }
        };

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Вы: ");
            Console.ResetColor();

            var userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (userInput.ToLower() == "exit")
                break;

            if (userInput.ToLower() == "clear")
            {
                messages = new List<Message>
                {
                    new Message { Role = "system", Content = "Ты - полезный ассистент" }
                };
                Console.WriteLine("История очищена.\n");
                continue;
            }

            // Добавляем сообщение пользователя
            messages.Add(new Message { Role = "user", Content = userInput });

            try
            {
                // Получаем ответ от GigaChat
                var assistantResponse = await GetChatCompletionAsync(messages);

                // Выводим ответ
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("GigaChat: ");
                Console.ResetColor();
                Console.WriteLine(assistantResponse);
                Console.WriteLine();

                // Добавляем ответ ассистента в историю
                messages.Add(new Message { Role = "assistant", Content = assistantResponse });
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static async Task<string> GetChatCompletionAsync(List<Message> messages)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);

        // Заголовки
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        request.Headers.Add("Accept", "application/json");

        // Тело запроса
        var requestBody = new ChatRequest
        {
            Model = "GigaChat",
            Messages = messages,
            Temperature = 0.7,
            MaxTokens = 1024
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        // Отправка запроса
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Парсинг ответа
        var responseJson = await response.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "Не удалось получить ответ";
    }
}

// Модели данных
public class AuthResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}

public class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = new List<Message>();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;
}

public class Message
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new List<Choice>();
}

public class Choice
{
    [JsonPropertyName("message")]
    public Message Message { get; set; } = new Message();
}