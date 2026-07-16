using System.Text;
using OpenAI.Models.Chat;
using Pandora.Agent;
using Pandora.Interfaces;
namespace Pandora.Models
{
    public class ChatToolCall : IToolCall
    {
        public string ToolName  { get; set; } = null!;
        public string Parameters { get; set; } = null!;
        public string ToolCallId { get; set; } = null!;

        public IList<JsonFieldChunk>? AddArguments(string arguments)
        {
            return null;
        }

        public void EnableStreamOutput()
        {
            return;
        }

        public ToolCall ToToolCall()
        {
            return new ToolCall
            {
                Id = ToolCallId,
                FunctionCall = new FunctionCall
                {
                    Name = ToolName,
                    Arguments = Parameters
                }
            };
        }
    }
    public class StreamToolCall : IToolCall
    {
        public string ToolName { get; set; } = null!;
        public string Parameters { get => sb.ToString();set => sb.Clear(); }
        public string ToolCallId { get; set; } = null!;
        private StringBuilder sb = new StringBuilder();
        private IncrementalJsonFieldParser? _reader = null;
        public IList<JsonFieldChunk>? AddArguments(string arguments)
        {
            sb.Append(arguments);
            if (_reader != null)
            {
                return _reader.Feed(arguments);
            }
            return null;
        }

        public ToolCall ToToolCall()
        {
            return new ToolCall
            {
                Id = ToolCallId,
                FunctionCall = new FunctionCall
                {
                    Name = ToolName,
                    Arguments = Parameters
                }
            };
        }

        public void EnableStreamOutput()
        {
            _reader = new IncrementalJsonFieldParser(true);
        }
    }
}