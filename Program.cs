using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExampleTestProject;

internal class Program
{
    private static readonly HttpClient _httpClient;
    private static string _accessToken = string.Empty;
    private static bool _jsonMode = false; // Флаг режима JSON
    private const string _clientId = "019adada-8f97-7bf5-8e46-2797e0c5f978";
    private const string _clientSecret = "secret";
    private const string _authUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    private const string _apiUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

    // JSON промпт для системного сообщения
    private const string JsonSystemPrompt = @"Ты - полезный ассистент. 
Всегда отвечай строго в следующем JSON формате:
{
    ""request"": ""[ЗДЕСЬ ПОВТОРИ ЗАПРОС ПОЛЬЗОВАТЕЛЯ]"",
    ""response"": ""[ЗДЕСЬ ТВОЙ ОТВЕТ НА ЗАПРОС]""
}
Важные правила:
1. Ответ должен быть ВАЛИДНЫМ JSON
2. Не добавляй никакого дополнительного текста кроме JSON
3. В поле ""request"" дословно повтори запрос пользователя
4. В поле ""response"" дай полный и развернутый ответ на запрос
5. Экранируй специальные символы в строках (кавычки, переносы строк и т.д.)
6. Ответ должен быть на том же языке, что и запрос";

    private const string NormalSystemPrompt = "Ты - полезный ассистент";

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
        Console.WriteLine("Введите 'json' для включения/выключения режима JSON.");
        Console.WriteLine();

        var messages = new List<Message>
        {
            new Message { Role = "system", Content = NormalSystemPrompt }
        };

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Вы: ");
            Console.ResetColor();

            var userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            // Обработка команд
            switch (userInput.ToLower())
            {
                case "exit":
                    return;

                case "clear":
                    messages =
                    [
                        new Message { Role = "system", Content = _jsonMode ? JsonSystemPrompt : NormalSystemPrompt }
                    ];
                    Console.WriteLine("История очищена.\n");
                    continue;

                case "json":
                    _jsonMode = !_jsonMode;

                    // Обновляем системный промпт
                    messages[0] = new Message
                    {
                        Role = "system",
                        Content = _jsonMode ? JsonSystemPrompt : NormalSystemPrompt
                    };

                    // Если есть история сообщений, перестраиваем ее с новым системным промптом
                    if (messages.Count > 1)
                    {
                        var historyMessages = messages.Skip(1).ToList();
                        messages = [messages[0]];
                        messages.AddRange(historyMessages);
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Режим JSON: {(_jsonMode ? "ВКЛЮЧЕН (ответы в JSON формате)" : "ВЫКЛЮЧЕН (обычный режим)")}");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
            }

            // Добавляем сообщение пользователя
            messages.Add(new Message { Role = "user", Content = userInput });

            try
            {
                // Получаем ответ от GigaChat
                var assistantResponse = await GetChatCompletionAsync(messages);

                // Обрабатываем ответ в зависимости от режима

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

                // При ошибке удаляем последнее сообщение пользователя из истории
                messages.RemoveAt(messages.Count - 1);
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
            Temperature = _jsonMode ? 0.3 : 0.7, // Ниже температура для более структурированных ответов в JSON режиме
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
    public List<Message> Messages { get; set; } = [];

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
    public List<Choice> Choices { get; set; } = [];
}

public class Choice
{
    [JsonPropertyName("message")]
    public Message Message { get; set; } = new();
}