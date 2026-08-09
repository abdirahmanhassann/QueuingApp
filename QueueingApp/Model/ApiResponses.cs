namespace QueueingApp.Model
{
    public class ApiResponses
    {
        public struct GenericResponse
        {
            public bool Result;
            public string ErrorMessage;
            public object Data;
        }
    }
}
