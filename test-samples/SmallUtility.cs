// Small utility file for testing
namespace SmallProject.Utils
{
    public static class StringHelper
    {
        public static string Capitalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return char.ToUpper(input[0]) + input.Substring(1);
        }

        public static bool IsValidEmail(string email)
        {
            return !string.IsNullOrEmpty(email) && email.Contains("@");
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}