using System;
using System.Text;

namespace PlayNow.Client.Services
{
    public static class StringProtector
    {
        // В Native AOT сборке этого простейшего Base64 хватит с головой, 
        // так как сам код компилируется в машинные инструкции.
        public static string Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

        // Если захочешь закодировать новую строку, используй эту функцию
        // (ее можно использовать просто для генерации строк при разработке)
        public static string Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }
    }
}