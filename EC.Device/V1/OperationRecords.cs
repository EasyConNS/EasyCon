namespace EC.Device;

public class OperationRecords
{
    private List<KeyStroke> records = new();
    string script = "";

    private static string GetScriptKeyName(string key)
    {
        return key switch
        {
            "RStick(128,0)" => "RS UP",
            "RStick(128,128)" => "RS RESET",
            "RStick(128,255)" => "RS DOWN",
            "RStick(0,128)" => "RS LEFT",
            "RStick(255,128)" => "RS RIGHT",
            "RStick(0,0)" => "RS 135",
            "RStick(255,255)" => "RS 315",
            "RStick(0,255)" => "RS 225",
            "RStick(255,0)" => "RS 45",
            "LStick(128,0)" => "LS UP",
            "LStick(128,128)" => "LS RESET",
            "LStick(128,255)" => "LS DOWN",
            "LStick(0,128)" => "LS LEFT",
            "LStick(255,128)" => "LS RIGHT",
            "LStick(0,0)" => "LS 135",
            "LStick(255,255)" => "LS 315",
            "LStick(0,255)" => "LS 225",
            "LStick(255,0)" => "LS 45",
            "HAT.TOP" => "UP",
            "HAT.BOTTOM" => "DOWN",
            "HAT.LEFT" => "LEFT",
            "HAT.RIGHT" => "RIGHT",
            "HAT.TOP_LEFT" => "UPLEFT",
            "HAT.TOP_RIGHT" => "UPRIGHT",
            "HAT.BOTTOM_LEFT" => "DOWNLEFT",
            "HAT.BOTTOM_RIGHT" => "DOWNRIGHT",
            _ => key,
        };
    }

    public void AddRecord(KeyStroke key)
    {
        string new_item = "";

        // insert the wait cmd
        if (records.Count() > 0)
        {
            long wait_time = (key.Time.Ticks - records.Last().Time.Ticks) / 10000;
            script += "WAIT " + wait_time + "\r\n";
        }

        records.Add(key);
        new_item += GetScriptKeyName(key.Key.Name);
        System.Diagnostics.Debug.WriteLine("keycode:" + key.KeyCode);
        if (key.KeyCode != 32 && key.KeyCode != 33)
        {
            if (key.Up)
            {
                new_item += " UP";
            }
            else
            {
                new_item += " DOWN";
            }
        }
        script += new_item + "\r\n";
    }

    public void Clear()
    {
        records.Clear();
        script = "";
    }
    public string ToScript(bool WithComment)
    {
        if (WithComment)
        {

        }

        return script;
    }
}

public enum RecordState
{
    RECORD_START = 0x00,
    RECORD_PAUSE = 0x01,
    RECORD_STOP = 0x02,
}
