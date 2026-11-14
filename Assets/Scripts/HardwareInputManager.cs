using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

public class HardwareInputManager : MonoBehaviour
{
    public static HardwareInputManager Instance { get; private set; }

    [Header("Configuración Serial (Arduino Mega)")]
    public bool usarArduino = false; // Déjalo desmarcado para probar con teclado
    public string nombrePuerto = "COM3"; // Cambia según el Administrador de Dispositivos
    public int baudios = 115200;

    [Header("Telemetría en Tiempo Real (Lectura)")]
    public bool[] botones = new bool[4];     // btn1..btn4
    public bool[] switches = new bool[2];    // sw1..sw2
    public Vector2 joystick;                 // joyX, joyY (-1.0 a 1.0)
    [Range(0f, 1f)] public float slider;     // Potenciómetro empuje (0.0 a 1.0)
    public int encoderPos;                   // Posición encoder KY-040
    public bool encoderBtn;                  // Botón del encoder
    public Vector2 gyroInclina;              // gyroX, gyroY (Pitch/Roll en grados)
    public int bpm = 75;                     // Pulso cardiaco MAX30102

    private SerialPort puertoSerial;
    private Thread hiloLectura;
    private bool ejecutando = false;
    private readonly ConcurrentQueue<string> colaTramas = new ConcurrentQueue<string>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (usarArduino) IniciarConexionSerial();
    }

    public void IniciarConexionSerial()
    {
        try
        {
            puertoSerial = new SerialPort(nombrePuerto, baudios);
            puertoSerial.ReadTimeout = 100;
            puertoSerial.Open();
            ejecutando = true;
            hiloLectura = new Thread(LeerPuertoEnSegundoPlano);
            hiloLectura.IsBackground = true;
            hiloLectura.Start();
            Debug.Log($"[Arduino] Conectado en {nombrePuerto}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Arduino] No se pudo abrir {nombrePuerto}: {e.Message}. Usando modo teclado.");
            usarArduino = false;
        }
    }

    private void LeerPuertoEnSegundoPlano()
    {
        while (ejecutando && puertoSerial != null && puertoSerial.IsOpen)
        {
            try
            {
                string linea = puertoSerial.ReadLine();
                if (!string.IsNullOrWhiteSpace(linea))
                    colaTramas.Enqueue(linea.Trim());
            }
            catch (TimeoutException) { }
            catch (Exception) { ejecutando = false; }
        }
    }

    void Update()
    {
        if (usarArduino)
        {
            while (colaTramas.TryDequeue(out string trama))
                ParsearTramaCSV(trama);
        }
        else
        {
            SimularConTeclado();
        }
    }

    // Formato esperado: btn1,btn2,btn3,btn4,sw1,sw2,joyX,joyY,slider,encPos,encBtn,gyroX,gyroY,bpm
    private void ParsearTramaCSV(string csv)
    {
        string[] datos = csv.Split(',');
        if (datos.Length < 14) return;

        try
        {
            for (int i = 0; i < 4; i++) botones[i] = datos[i] == "1";
            switches[0] = datos[4] == "1";
            switches[1] = datos[5] == "1";
            joystick.x = float.Parse(datos[6], System.Globalization.CultureInfo.InvariantCulture);
            joystick.y = float.Parse(datos[7], System.Globalization.CultureInfo.InvariantCulture);
            slider = Mathf.Clamp01(float.Parse(datos[8], System.Globalization.CultureInfo.InvariantCulture));
            encoderPos = int.Parse(datos[9]);
            encoderBtn = datos[10] == "1";
            gyroInclina.x = float.Parse(datos[11], System.Globalization.CultureInfo.InvariantCulture);
            gyroInclina.y = float.Parse(datos[12], System.Globalization.CultureInfo.InvariantCulture);
            bpm = int.Parse(datos[13]);
        }
        catch { /* Ignorar tramas corruptas al iniciar conexión */ }
    }

    private void SimularConTeclado()
    {
        // Botones Arcade: Teclas 1, 2, 3, 4
        botones[0] = Input.GetKey(KeyCode.Alpha1);
        botones[1] = Input.GetKey(KeyCode.Alpha2);
        botones[2] = Input.GetKey(KeyCode.Alpha3);
        botones[3] = Input.GetKey(KeyCode.Alpha4);

        // Switches palanca: Teclas Q y E (alternan estado al presionar)
        if (Input.GetKeyDown(KeyCode.Q)) switches[0] = !switches[0];
        if (Input.GetKeyDown(KeyCode.E)) switches[1] = !switches[1];

        // Joystick traslación: WASD
        joystick.x = Input.GetAxis("Horizontal");
        joystick.y = Input.GetAxis("Vertical");

        // Potenciómetro empuje: Flechas Arriba / Abajo
        if (Input.GetKey(KeyCode.UpArrow)) slider = Mathf.Clamp01(slider + Time.deltaTime * 0.5f);
        if (Input.GetKey(KeyCode.DownArrow)) slider = Mathf.Clamp01(slider - Time.deltaTime * 0.5f);

        // Encoder frecuencia: Flechas Izquierda / Derecha + Espacio para clic
        if (Input.GetKeyDown(KeyCode.RightArrow)) encoderPos++;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) encoderPos--;
        encoderBtn = Input.GetKey(KeyCode.Space);

        // Giroscopio: Z / X para inclinar
        gyroInclina.x = Input.GetKey(KeyCode.Z) ? -15f : (Input.GetKey(KeyCode.X) ? 15f : 0f);

        // Simular pulso cardiaco: Tecla B sube el estrés
        bpm = Input.GetKey(KeyCode.B) ? 115 : 76;
    }

    // Enviar comando hacia el Arduino (ej. "VIB:1", "LED:2", "OLED:FREQ_88.5")
    public void EnviarComandoArduino(string comando)
    {
        if (usarArduino && puertoSerial != null && puertoSerial.IsOpen)
        {
            try { puertoSerial.WriteLine(comando); }
            catch (Exception e) { Debug.LogWarning($"Error enviando a Arduino: {e.Message}"); }
        }
        else
        {
            Debug.Log($"[Simulación Salida Arduino] -> {comando}");
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(15, 15, 310, 210), "TELEMETRÍA CABINA ASTRONAUTA JR.");
        GUI.Label(new Rect(25, 40, 290, 25), $"Modo: {(usarArduino ? "ARDUINO (" + nombrePuerto + ")" : "TECLADO SIMULADO")}");
        GUI.Label(new Rect(25, 65, 290, 25), $"Botones [1-4]: {(botones[0]?"[1]":"-")} {(botones[1]?"[2]":"-")} {(botones[2]?"[3]":"-")} {(botones[3]?"[4]":"-")}");
        GUI.Label(new Rect(25, 90, 290, 25), $"Switches [Q/E]: SW1={(switches[0]?"ON":"OFF")} | SW2={(switches[1]?"ON":"OFF")}");
        GUI.Label(new Rect(25, 115, 290, 25), $"Joystick [WASD]: X={joystick.x:F2} , Y={joystick.y:F2}");
        GUI.Label(new Rect(25, 140, 290, 25), $"Empuje [Flechas Arriba/Abajo]: {(slider * 100f):F0}%");
        GUI.Label(new Rect(25, 165, 290, 25), $"Encoder [Izq/Der + Espacio]: {encoderPos} {(encoderBtn ? "(CLICK)" : "")}");
        GUI.Label(new Rect(25, 190, 290, 25), $"Giroscopio [Z/X]: {gyroInclina.x:F0}° | Pulso [B]: {bpm} BPM");
    }

    void OnApplicationQuit()
    {
        ejecutando = false;
        if (puertoSerial != null && puertoSerial.IsOpen) puertoSerial.Close();
    }
}