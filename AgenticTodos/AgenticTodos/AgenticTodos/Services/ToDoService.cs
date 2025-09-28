using AgenticTodos.Shared;

namespace AgenticTodos.Services
{
    public class ToDoService
    {

        private List<Todo> _todos = new List<Todo>();

        public void Add(Todo todo) => _todos.Add(todo);

        public List<Todo> Get() => new List<Todo>(_todos);

    }
}
