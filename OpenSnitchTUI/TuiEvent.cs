namespace OpenSnitchTUI
{
    public class TuiEvent
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = ""; // Ping, Alert, Rule
        public string Protocol { get; set; } = "";
        public string Source { get; set; } = ""; // Process
        public string Pid { get; set; } = ""; // Process ID
        
        // Split Destination
        public string DestinationIp { get; set; } = "";
        public string DestinationPort { get; set; } = "";
        
        public string Details { get; set; } = "";
    }
}