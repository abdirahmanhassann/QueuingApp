using static QueueingApp.Model.ApiResponses;

namespace QueueingApp.Helpers
{
    public class ApiHelper
    {
        public const string GENERIC_ERROR = "A non-specific error occurred. Please contact support for assistance.";
        public const string TOKEN_ERROR = "Token invalid or expired";

        public static GenericResponse CreateSuccessResponseNoData()
        {
            GenericResponse resp = new GenericResponse()
            {
                Result = true
            };

            return resp;
        }

        public static GenericResponse CreateSuccessResponseWithData(object data)
        {
            GenericResponse resp = new GenericResponse()
            {
                Result = true,
                Data = data
            };

            return resp;
        }

        public static GenericResponse CreateFailureResponse(string message)
        {
            GenericResponse resp = new GenericResponse()
            {
                Result = false,
                ErrorMessage = message
            };

            return resp;
        }
    }
}
