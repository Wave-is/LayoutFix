namespace UI_TestApp;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
        
        var txt = new TextBox 
        { 
            Dock = DockStyle.Fill, 
            Multiline = true,
            Font = new Font("Segoe UI", 16)
        };
        
        txt.TextChanged += (s, e) =>
        {
            try { System.IO.File.WriteAllText("output.txt", txt.Text); } catch { }
        };
        
        this.Controls.Add(txt);
        this.ActiveControl = txt;
    }
}
