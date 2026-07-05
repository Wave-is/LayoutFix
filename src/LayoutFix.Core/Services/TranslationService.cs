using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Web;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class TranslationService : ITranslationService
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string encodedText = HttpUtility.UrlEncode(text);
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLanguage}&dt=t&q={encodedText}";

            string response = await _httpClient.GetStringAsync(url);
            
            var node = JsonNode.Parse(response);
            if (node is JsonArray rootArray && rootArray.Count > 0 && rootArray[0] is JsonArray sentencesArray)
            {
                string result = "";
                foreach (var sentenceNode in sentencesArray)
                {
                    if (sentenceNode is JsonArray sentence && sentence.Count > 0)
                    {
                        result += sentence[0]?.ToString() ?? "";
                    }
                }
                return result.Trim();
            }

            return "Translation failed.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
