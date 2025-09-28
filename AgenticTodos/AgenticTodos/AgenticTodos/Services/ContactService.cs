using AgenticTodos.Shared;

namespace AgenticTodos.Services
{
    public class ContactService
    {
        public List<Contact> Contacts = new List<Contact>
        {
            new Contact
            {
                id= Guid.NewGuid().ToString(),
                FirstName = "Peter",
                LastName = "Parker"
            },
            new Contact
            {
                id= Guid.NewGuid().ToString(),
                FirstName = "Tony",
                LastName = "Stark"
            },
            new Contact
            {
                id= Guid.NewGuid().ToString(),
                FirstName = "Bruce",
                LastName = "Banner"
            }
        };
    }
}
