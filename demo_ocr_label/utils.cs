using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace demo_ocr_label
{
    public static class utils
    {
        public static Config? fileConfig = null;
        // Static constructor - tự động chạy khi class được load lần đầu
        static utils()
        {
            LoadConfigFile("config.json");
        }
        public static void LoadConfigFile(string configFileName)
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
            Debug.WriteLine("Đường dẫn config file: " + filePath);

            if (!File.Exists(filePath))
            {
                Debug.WriteLine("Không tìm thấy file config!");
                // Tạo config mặc định
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<Config>(jsonString);

                if (config == null)
                {
                    throw new Exception("Không thể đọc file JSON (null config).");
                }

                // Đảm bảo các property không null
                if (config.systemArivable == null)
                    config.systemArivable = new SystemArivables();
                if (config.labelRectangle == null)
                    config.labelRectangle = new LabelRectangle();
                if (config.modelParams == null)
                    config.modelParams = new PaddleOCRParams();

                Debug.WriteLine("Đọc dữ liệu thành công từ file config!");
                fileConfig = config; // QUAN TRỌNG: Phải gán vào fileConfig

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Lỗi khi đọc file config:\n" + ex.Message);
                // Tạo config mặc định khi lỗi
            }
        }
    }
}
