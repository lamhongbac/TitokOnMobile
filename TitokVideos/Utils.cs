using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;

namespace TitokVideos
{
    public static class Utils
    {
        /// <summary>
        /// Hàm getUseriD()=>secUid để gọi API lấy thông tin secUid của Gigamall Vietnam=gigamallvietnam
        /// </summary>
        /// <returns></returns>
        public static async Task<string> GetUseriD()
        {
            

            //Console.WriteLine("Đang gọi API lấy thông tin User của Gigamall Vietnam...");

            using var client = new HttpClient();
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                // Tham số uniqueId đã được cập nhật thành gigamallvietnam
                RequestUri = new Uri("https://tiktok-api23.p.rapidapi.com/api/user/info?uniqueId=gigamallvietnam"),
                Headers =
                    {
                        { "x-rapidapi-host", "tiktok-api23.p.rapidapi.com" },
                        { "x-rapidapi-key", "f5836d2ed0msh35455f28992a930p1d0f9fjsnf0faf50ad058" },
                    }
            };

            try
            {
                using var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();

                //Console.WriteLine("THÀNH CÔNG! Hãy tìm chuỗi 'secUid' trong kết quả dưới đây:");
                //Console.WriteLine(body);
                return body;
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"LỖI GỌI API: {ex.Message}");
                return null;
            }
        }

    }
}
