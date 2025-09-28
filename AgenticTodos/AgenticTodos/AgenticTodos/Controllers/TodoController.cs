using AgenticTodos.Services;
using AgenticTodos.Shared;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgenticTodos.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TodoController : ControllerBase
    {
        private readonly ContactService _contactService;
        private readonly ToDoService _toDoService;

        public TodoController(ContactService contactService, ToDoService toDoService)
        {
            _contactService = contactService;
            _toDoService = toDoService;
        }

        [HttpGet]
        [Route(nameof(GetToDos))]
        public IActionResult GetToDos()
        {
            return Ok(JsonSerializer.Serialize(_toDoService.Get()));
        }

        [HttpPost]
        [Route(nameof(CreateToDo))]
        public async Task<IActionResult> CreateToDo(CreateToDoRequest req)
        {
            string apiKey = Environment.GetEnvironmentVariable("OpenAPIKey")!;
            var chat = new ChatClient("gpt-5-nano", apiKey);

            // Tool 1: find_contact
            var findContactTool = ChatTool.CreateFunctionTool(
                functionName: "find_contact",
                functionDescription: "Find a contact by name or partial name. Returns best match or null.",
                functionParameters: BinaryData.FromString("""
                                    {
                                      "type": "object",
                                      "properties": {
                                        "query": { "type": "string", "description": "Name or partial, e.g. 'steve'" }
                                      },
                                      "required": ["query"],
                                      "additionalProperties": false
                                    }
                                    """));

            // Tool 2: create_todo
            var createTodoTool = ChatTool.CreateFunctionTool(
                functionName: "create_todo",
                functionDescription: "Create a TODO with a natural - language due date.Server resolves the date.",
                
                functionParameters: BinaryData.FromString("""
                                    {
                                      "type": "object",
                                      "properties": {
                                        "title": { "type": "string", "description": "Short imperative title, e.g. 'Call Steve'" },
                                        "content" : { "type": "string", "description": "full description of the task" },
                                        "dueDate": { "type": "string", "description": "Natural text like 'Friday', 'tomorrow 2pm'" },
                                        "contactId": { "type": "string", "description": "Optional contact id from find_contact" }
                                      },
                                      "required": ["title", "dueDateText"],
                                      "additionalProperties": false
                                    }
                                    """));


            var userText = req.Prompt;


            var system =
        @"You turn user requests into TODOs using tools.
Rules:
- If a person is mentioned, call find_contact first with the name.
- Then call create_todo with a concise title and a dueDateText extracted from the request. This due date should be a C# formatted datetime
- Use contactId from find_contact if a suitable match exists (name similarity).
- Be brief and confirm the todo created with date in local words (e.g., 'Friday 9:00 AM'). The message must start with todo created";

            var messages = new List<ChatMessage>
    {
        new SystemChatMessage(system),
        new UserChatMessage(userText)
    };

            var options = new ChatCompletionOptions
            {
                Tools = { findContactTool, createTodoTool }
                // no ToolChoice: let the model decide
            };

            while (true)
            {
                ClientResult<ChatCompletion> resp = await chat.CompleteChatAsync(messages, options);

                // If the model asked to call a tool, execute & loop
                if (resp.Value.FinishReason == ChatFinishReason.ToolCalls && resp.Value.ToolCalls.Count > 0)
                {
                    messages.Add(new AssistantChatMessage(resp));

                    foreach (var call in resp.Value.ToolCalls)
                    {
                        object result = call.FunctionName switch
                        {
                            "find_contact" => HandleFindContact((JsonObject)JsonNode.Parse(call.FunctionArguments)!),
                            "create_todo" => HandleCreateTodo((JsonObject)JsonNode.Parse(call.FunctionArguments)!),
                            _ => new { error = "Unknown function" }
                        };

                        messages.Add(new ToolChatMessage(call.Id, JsonSerializer.Serialize(result)));
                    }

                    continue;
                }

                // Final answer for the user (assistant text)
                var text = resp.Value.Content.FirstOrDefault()?.Text ?? "OK";
                return Ok(text);
            }
        }


        Contact HandleFindContact(JsonObject callArgs)
        {
            string query = callArgs["query"]!.GetValue<string>();

            return _contactService.Contacts
                .OrderByDescending(c => $"{c.FirstName} {c.LastName}".Contains(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => $"{c.FirstName} {c.LastName}")
                .FirstOrDefault(c => $"{c.FirstName} {c.LastName}".Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Todo HandleCreateTodo(JsonObject callArgs)
        {
            string title = callArgs["title"]!.GetValue<string>();
            string content = callArgs["content"]?.GetValue<string>() ?? title;
            string dueDate = callArgs["dueDate"]!.GetValue<string>();
            string contactId = callArgs["contactId"]!.GetValue<string>();


            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Kentucky/Louisville");
            var due = ResolveDueDate(dueDate, tz);
            var todo = new Todo
            {
                Title = title,
                Content = content,
                DueDate = due,
                ContactId = contactId
            };

            _toDoService.Add(todo);

            return todo;
        }

        DateTime ResolveDueDate(string dueDateText, TimeZoneInfo tz)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, tz);

            var weekdays = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunday"] = DayOfWeek.Sunday,
                ["monday"] = DayOfWeek.Monday,
                ["tuesday"] = DayOfWeek.Tuesday,
                ["wednesday"] = DayOfWeek.Wednesday,
                ["thursday"] = DayOfWeek.Thursday,
                ["friday"] = DayOfWeek.Friday,
                ["saturday"] = DayOfWeek.Saturday
            };

            var trimmed = dueDateText.Trim();
            if (weekdays.TryGetValue(trimmed, out var targetDay))
            {
                int diff = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
                var date = now.Date.AddDays(diff);
                var local = new DateTimeOffset(date.Year, date.Month, date.Day, 9, 0, 0, now.Offset);
                return TimeZoneInfo.ConvertTime(local, tz).DateTime;
            }

            if (DateTimeOffset.TryParse(trimmed, out var parsed))
            {
                return TimeZoneInfo.ConvertTime(parsed, tz).DateTime;
            }

            return now.Date.AddDays(1).AddHours(9);
        }

    }
}
