using CommandLine; 
using System;
using System.Collections.Generic;
using System.Diagnostics; 
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;

namespace EffectiveMobile_test
{

    public class DateConverter
    {
        public static DateTime ConvertDate(string dateString)
        {
            // Проверяем, соответствует ли строка формату 
            if (IsValidDateFormat(dateString, "dd.MM.yyyy", "yyyy.MM.dd"))
            {
                // Если строка соответствует одному из форматов, преобразуем её в DateTime
                DateTime dateTime;
                if (DateTime.TryParseExact(dateString, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                {
                    return dateTime;
                }
                else
                {
                    return DateTime.ParseExact(dateString, "yyyy.MM.dd", CultureInfo.InvariantCulture);
                }
            }
            else
            {
                // Если строка не соответствует ни одному из форматов
                throw new ArgumentException("Некорректный формат даты и времени.");
            }
        }

        private static bool IsValidDateFormat(string dateString, string format1, string format2)
        {
            // Пытаемся преобразовать строку в DateTime с указанными форматами
            DateTime dateTime;
            if (DateTime.TryParseExact(dateString, format1, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            {
                return true;
            }
            else if (DateTime.TryParseExact(dateString, format2, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
    public class IpAddressValidator
    {
        public static bool IsValidIpAddress(string ipAddress)
        {
            if (IPAddress.TryParse(ipAddress, out IPAddress ip))
            {
                return true;
            }
            else
            {
                throw new ArgumentException("Некорректный формат IP адреса.");
            }
        }
    }

    // Реализуем параметры командной строки
    class Options
    {
        [Option('l', "file-log", Required = true, HelpText = "путь к файлу с логами")]
        public string LogFile { get; set; }

        [Option('o', "file-output", Required = true, HelpText = "путь к файлу с результатом\r\n")]
        public string OutputFile { get; set; }

        [Option('s',"address-start", Required = false, HelpText = "нижняя граница диапазона адресов, " +
            "необязательный параметр, по умолчанию обрабатываются все адреса")]
        public string AddressStart { get; set; }

        [Option('m',"address-mask", Required = false, Default = "0.0.0.0", HelpText = "верхнаяя граница диапазона адресов, " +
                                                                                      " необязательный параметр, по умолчанию обрабатываются все адреса" +
                                                                                      " Параметр нельзя использовать, если не задан address-start")]
        public string AddressMask { get; set; }

        [Option("time-start", Required = true, HelpText = "нижняя граница временного интервала\r\n")]
        public string TimeStart { get; set; }

        [Option("time-end", Required = true, HelpText = "верхняя граница временного интервала.\r\n")]
        public string TimeEnd { get; set; }
    }

    class LogEntry
    {
        public string IP { get; set; }
        public DateTime Time { get; set; }
    }


    internal class Program
    {

        private static bool IsInSubnet(IPAddress ip, IPAddress subnet, IPAddress mask)
        {
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] subnetBytes = subnet.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();

            for (int i = 0; i < ipBytes.Length; i++)
            {
                if ((ipBytes[i] & maskBytes[i]) != (subnetBytes[i] & maskBytes[i]))
                {
                    return false;
                }
            }
            return true;
        }

        static void Main(string[] args)
        {
            // Парсим параметры коммандной строки из Options
            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(opts => RunOptions(opts));
        }


        static void RunOptions(Options opts)
        {

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                // Конверитируем даты
                DateTime TimeStart = DateConverter.ConvertDate(opts.TimeStart);
                DateTime TimeEnd = DateConverter.ConvertDate(opts.TimeEnd);

                // Проверяем IP адрес и маску
                if (opts.AddressStart == null && opts.AddressMask != null) 
                { Console.WriteLine("    Ошибка: Нельзя использовать параметр 'm, address-mask' без 's, adress-start'!"); return; }


                List<LogEntry> logEntries = new List<LogEntry>(); 
                using (StreamReader reader = new StreamReader(opts.LogFile))

                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Split(new char[] { ':' }, 2);
                        string IP = parts[0];
                        DateTime date = DateTime.Parse(parts[1]);

                        // Добавляем в logEntry
                        LogEntry logEntry = new LogEntry { IP = IP, Time = date };
                        logEntries.Add(logEntry);
                    }
                }

                var filteredEntries = logEntries.Where(e => e.Time >= TimeStart && e.Time <= TimeEnd).GroupBy(e => e.IP).Select(g => new { IP = g.Key, Count = g.Count() });

                if (IpAddressValidator.IsValidIpAddress(opts.AddressStart))
                {
                    // Сортируем по IP
                    var sortedByIP = filteredEntries.OrderBy(e => e.IP).ToList();

                    if (IpAddressValidator.IsValidIpAddress(opts.AddressMask))
                    {
                        // фильтуем от AddressStart до AddressMask
                        var filteredByIp = sortedByIP.Where(e => IsInSubnet(IPAddress.Parse(e.IP), IPAddress.Parse(opts.AddressStart), IPAddress.Parse(opts.AddressMask)));
                        filteredEntries = filteredByIp.ToList();

                    }
                    else return;
                }
                
                // Записываем результат в файл
                using (StreamWriter writer = new StreamWriter(opts.OutputFile))
                {
                    foreach (var entry in filteredEntries)
                    {
                        writer.WriteLine($"{entry.IP};{entry.Count}");
                    }
                }

                sw.Stop();

                // Выводим отчет
                Console.WriteLine($"\nВременной интервал: {TimeStart} - {TimeEnd}");
                Console.WriteLine($"Диапазон адресов: {IPAddress.Parse(opts.AddressStart)} - {IPAddress.Parse(opts.AddressMask)}");
                Console.WriteLine($"        Совпадений: {filteredEntries.Count()}");
                Console.WriteLine($"  Время выполнения: {sw.ElapsedMilliseconds} мс");
                Console.WriteLine(" Результат сохранен в {0}", opts.OutputFile);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Произошла ошибка: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
            finally
            {

            }
        }
    }
}
