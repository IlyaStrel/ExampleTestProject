using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExampleTestProject;

internal class Program
{
    private static readonly HttpClient _httpClient;
    private static string _accessToken = string.Empty;
    private static bool _dataCollected = false; // Флаг сбора данных
    private static PatientContext _patientContext = new(); // Контекст пациента
    private const string _clientId = "019adada-8f97-7bf5-8e46-2797e0c5f978";
    private const string _clientSecret = "secret";
    private const string _authUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    private const string _apiUrl = "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

    // Системный промпт для врача-терапевта
    private const string SystemPrompt = @"Ты — врач-терапевт. Твоя задача — собрать анамнез и предоставить рекомендации.

ПРАВИЛА РАБОТЫ:
1. Всегда начинай с вопроса о главной жалобе
2. Задавай уточняющие вопросы по схеме:
   - О симптомах: начало, характер, длительность, интенсивность, локализация
   - О сопутствующих симптомах
   - О хронических заболеваниях
   - Об аллергиях и лекарствах
3. НЕ давай финальный ответ, пока не соберешь достаточно информации
4. После сбора данных дай структурированный ответ:
   - Сводка жалоб
   - Предполагаемый диагноз (дифференциальный)
   - Рекомендации по обследованию
   - Что делать сейчас
   - Когда срочно к врачу
5. Всегда добавляй: 'Это не заменяет очный осмотр врача'
6. Задавай по одному-два вопроса за раз, не перегружай пациента
7. Если информация уже собрана в предыдущих ответах — не спрашивай повторно
8. Если пациент говорит 'всё', 'достаточно', 'закончили' — переходи к финальному ответу

ФОРМАТ ДИАЛОГА:
- Задавай вопросы естественно, как врач на приеме
- Уточняй непонятные моменты
- Проявляй эмпатию, но будь профессиональным";

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
            }
        };

        _httpClient = new HttpClient(handler);
    }

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Медицинский консультант (терапевт) ===");
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
        Console.WriteLine("Консультация терапевта начата.");
        Console.WriteLine("Команды: 'exit' - выход, 'clear' - новая консультация, 'summary' - показать собранные данные");
        Console.WriteLine();

        var messages = new List<Message>
        {
            new Message { Role = "system", Content = SystemPrompt }
        };

        // Добавляем приветственное сообщение от ассистента
        var greeting = await GetChatCompletionAsync(messages, "Я готов помочь. Расскажите, что вас беспокоит?");
        messages.Add(new Message { Role = "assistant", Content = greeting });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Врач: {greeting}");
        Console.ResetColor();
        Console.WriteLine();

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Пациент: ");
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
                    messages = [new Message { Role = "system", Content = SystemPrompt }];
                    _dataCollected = false;
                    _patientContext = new PatientContext();
                    Console.WriteLine("\nНачинаем новую консультацию...\n");

                    // Новое приветствие
                    greeting = await GetChatCompletionAsync(messages, "Я готов помочь. Расскажите, что вас беспокоит?");
                    messages.Add(new Message { Role = "assistant", Content = greeting });

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Врач: {greeting}");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;

                case "summary":
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n=== СОБРАННЫЕ ДАННЫЕ ===");
                    Console.WriteLine(_patientContext);
                    Console.WriteLine("=====================\n");
                    Console.ResetColor();
                    continue;
            }

            // Добавляем сообщение пользователя в историю
            messages.Add(new Message { Role = "user", Content = userInput });

            // Обновляем контекст пациента
            _patientContext.AddPatientResponse(userInput);

            try
            {
                // Получаем ответ от врача
                var doctorResponse = await GetChatCompletionAsync(messages);

                // Проверяем, является ли ответ финальным
                if (IsFinalResponse(doctorResponse))
                {
                    _dataCollected = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n=== ФИНАЛЬНЫЙ ОТВЕТ ВРАЧА ===");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Врач: ");
                Console.ResetColor();
                Console.WriteLine(doctorResponse);

                Console.WriteLine();

                // Добавляем ответ врача в историю
                messages.Add(new Message { Role = "assistant", Content = doctorResponse });

                // Если это финальный ответ, сохраняем его в контексте
                if (_dataCollected)
                {
                    _patientContext.FinalRecommendations = doctorResponse;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Консультация завершена. Введите 'clear' для новой консультации.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка: {ex.Message}");
                Console.ResetColor();

                // При ошибке удаляем последнее сообщение пользователя из истории
                messages.RemoveAt(messages.Count - 1);
                _patientContext.RemoveLastResponse();
            }
        }
    }

    private static bool IsFinalResponse(string response)
    {
        // Проверяем по ключевым фразам, что это финальный структурированный ответ
        var finalIndicators = new[]
        {
            "Сводка жалоб",
            "Предполагаемый диагноз",
            "Рекомендации по обследованию",
            "Что делать сейчас",
            "Когда срочно к врачу",
            "1)", "2)", "3)", "4)", "5)", // Нумерованные пункты
            "не заменяет очный осмотр"
        };

        return finalIndicators.Any(indicator =>
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> GetChatCompletionAsync(List<Message> messages, string initialPrompt = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);

        // Заголовки
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        request.Headers.Add("Accept", "application/json");

        // Если передали initialPrompt, используем его вместо полного цикла
        if (initialPrompt != null)
        {
            return initialPrompt;
        }

        // Тело запроса
        var requestBody = new ChatRequest
        {
            Model = "GigaChat",
            Messages = messages,
            Temperature = 0.3, // Низкая температура для более последовательных медицинских ответов
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

// Класс для хранения контекста пациента
public class PatientContext
{
    public DateTime ConsultationStart { get; } = DateTime.Now;
    public string MainComplaint { get; private set; } = string.Empty;
    public List<string> Symptoms { get; } = new();
    public List<string> ChronicDiseases { get; } = new();
    public List<string> Allergies { get; } = new();
    public List<string> Medications { get; } = new();
    public List<string> PatientResponses { get; } = new();
    public string FinalRecommendations { get; set; } = string.Empty;

    public void AddPatientResponse(string response)
    {
        PatientResponses.Add($"[{DateTime.Now:HH:mm:ss}] {response}");

        // Простая эвристика для извлечения информации
        // В реальном приложении здесь можно использовать NLP
        if (string.IsNullOrEmpty(MainComplaint) && PatientResponses.Count == 1)
        {
            MainComplaint = response;
        }

        // Проверяем на упоминание хронических болезней
        if (response.Contains("хроническ", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("болею", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("диагноз", StringComparison.OrdinalIgnoreCase))
        {
            ChronicDiseases.Add(response);
        }

        // Проверяем на упоминание аллергий
        if (response.Contains("аллерг", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("не переношу", StringComparison.OrdinalIgnoreCase))
        {
            Allergies.Add(response);
        }

        // Проверяем на упоминание лекарств
        if (response.Contains("принимаю", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("лекарств", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("таблет", StringComparison.OrdinalIgnoreCase))
        {
            Medications.Add(response);
        }

        // Сбор симптомов (кроме первого ответа)
        if (PatientResponses.Count > 1)
        {
            Symptoms.Add(response);
        }
    }

    public void RemoveLastResponse()
    {
        if (PatientResponses.Count > 0)
        {
            PatientResponses.RemoveAt(PatientResponses.Count - 1);
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Консультация начата: {ConsultationStart:HH:mm:ss}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(MainComplaint))
        {
            sb.AppendLine($"Главная жалоба: {MainComplaint}");
        }

        if (Symptoms.Count > 0)
        {
            sb.AppendLine($"\nСимптомы ({Symptoms.Count}):");
            foreach (var symptom in Symptoms)
            {
                sb.AppendLine($"  - {symptom}");
            }
        }

        if (ChronicDiseases.Count > 0)
        {
            sb.AppendLine($"\nХронические заболевания ({ChronicDiseases.Count}):");
            foreach (var disease in ChronicDiseases)
            {
                sb.AppendLine($"  - {disease}");
            }
        }

        if (Allergies.Count > 0)
        {
            sb.AppendLine($"\nАллергии ({Allergies.Count}):");
            foreach (var allergy in Allergies)
            {
                sb.AppendLine($"  - {allergy}");
            }
        }

        if (Medications.Count > 0)
        {
            sb.AppendLine($"\nЛекарства ({Medications.Count}):");
            foreach (var med in Medications)
            {
                sb.AppendLine($"  - {med}");
            }
        }

        if (!string.IsNullOrEmpty(FinalRecommendations))
        {
            sb.AppendLine($"\n=== ФИНАЛЬНЫЕ РЕКОМЕНДАЦИИ ===");
            sb.AppendLine(FinalRecommendations);
        }

        sb.AppendLine($"\nВсего ответов пациента: {PatientResponses.Count}");

        return sb.ToString();
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