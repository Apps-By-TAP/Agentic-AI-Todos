namespace AgenticTodos.Shared
{
    public class CreateToDoResponse
    {
        public bool Success { get; set; }
        public Todo ToDo { get; set; }
        public string ErrorMessage { get; set; }
    }
}
