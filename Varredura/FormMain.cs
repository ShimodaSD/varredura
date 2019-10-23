using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HtmlDesktop
{
    public partial class FormMain : Form
    {
        public class Noticia
        {
            public long Id { get; set; }
            public string Notificacao { get; set; }
            public string DataStr { get; set; }
            public string Titulo { get; set; }
            public string Link { get; set; }
            public DateTime Data { get; set; }
            public bool Analisar { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        private List<Noticia> noticias;
        private string banco;

        private int ParseLinha(string html, int inicio, List<Noticia> lista)
        {
            int fimLinha = html.IndexOf("</tr>", inicio);
            if (fimLinha < 0)
                return -1;

            int fim = html.IndexOf("</td>", inicio);
            if (fim < 0 || fim > fimLinha)
                return -1;

            string data = html.Substring(inicio, fim - inicio).Trim();

            int classA = html.IndexOf("class=\"titulo_noticias\"", inicio);
            if (classA < 0 || classA > fimLinha)
                return -1;

            int href = html.IndexOf("href=\"", classA);
            if (href < 0 || href > fimLinha)
                return -1;
            href += 6;

            int fimHref = html.IndexOf('\"', href);
            if (fimHref < 0 || fimHref > fimLinha)
                return -1;

            string link = html.Substring(href, fimHref - href).Trim();

            int inicioTitulo = html.IndexOf('>', fimHref);
            if (inicioTitulo < 0 || inicioTitulo > fimLinha)
                return -1;
            inicioTitulo++;

            int fimTitulo = html.IndexOf('<', inicioTitulo);
            if (fimTitulo < 0 || fimTitulo > fimLinha)
                return -1;

            string titulo = html.Substring(inicioTitulo, fimTitulo - inicioTitulo).Trim();

            data = System.Web.HttpUtility.HtmlDecode(data);
            DateTime.TryParseExact(data, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime d);

            lista.Add(new Noticia()
            {
                Titulo = System.Web.HttpUtility.HtmlDecode(titulo),
                DataStr = data,
                Link = System.Web.HttpUtility.HtmlDecode(link),
                Data = d
            });

            return fimLinha;
        }

        private List<Noticia> ParseNoticias(string html)
        {
            List<Noticia> lista = new List<Noticia>();

            int inicio = 0;

            do
            {
                inicio = html.IndexOf("<span class=\"icon-calendar\"></span>", inicio);
                if (inicio > 0)
                    inicio = ParseLinha(html, inicio + 35, lista);
            } while (inicio > 0);

            return lista;
        }

        public FormMain()
        {
            InitializeComponent();
            string executavel = GetType().Assembly.Location;

            string valor = null;
            try
            {
                valor = Microsoft.Win32.Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", "HtmlDesktop", "") as string;
            }
            catch
            {
            }

            if (executavel != valor)
            {
                try
                {
                    Microsoft.Win32.Registry.SetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", "HtmlDesktop", executavel, Microsoft.Win32.RegistryValueKind.String);
                }
                catch
                {
                }
            }


            banco = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(executavel), "banco.bin");

            noticias = new List<Noticia>();

            using (var db = new LiteDB.LiteDatabase(banco))
            {
                var col = db.GetCollection<Noticia>();
                noticias.AddRange(col.FindAll());
            }

            Atualizar();

            notifyIcon.Icon = Icon;
        }

        private bool AnalisarNoticia(Noticia noticia)
        {
            string html;
            using (System.Net.WebClient client = new System.Net.WebClient())
            {
                html = System.Web.HttpUtility.HtmlDecode(client.DownloadString(noticia.Link));
            }

            

            if (html.Contains("WACC"))
            {
                noticia.Notificacao = "WACC";
                return true;
            }
            else if(html.Contains("Taxa de remuneração regulatória")|| html.Contains("taxa de remuneração regulatoria")|| html.Contains("taxa de remuneração") || html.Contains("taxa regulatória"))
            {
                noticia.Notificacao = "Taxa de remuneração regulatória";
                return true;
            }
            else
            {
                noticia.Notificacao = "";
                return false;
            }            
        }

        private void Atualizar()
        {
            string html;
            using (System.Net.WebClient client = new System.Net.WebClient())
            {
                html = client.DownloadString("https://www2.aneel.gov.br/aplicacoes_liferay/noticias_area/?idAreaNoticia=425&");                
            }

            List<Noticia> novasNoticias = ParseNoticias(html);

            bool notificarBalao = false;

            if (noticias != null)
            {
                foreach (Noticia nova in novasNoticias)
                {
                    bool analisar = true;
                    foreach (Noticia velha in noticias)
                    {
                        if (velha.Titulo.Equals(nova.Titulo, StringComparison.OrdinalIgnoreCase))
                        {
                            nova.Notificacao = velha.Notificacao;
                            analisar = false;
                            break;
                        }
                    }
                    nova.Analisar = analisar;
                    if (analisar)
                        notificarBalao |= AnalisarNoticia(nova);
                }
            }

            noticias = novasNoticias;

            listView.Items.Clear();

            foreach (Noticia noticia in noticias)
            {
                if (noticia.Analisar)
                    AnalisarNoticia(noticia);
                var item = listView.Items.Add(noticia.Notificacao ?? "");
                item.SubItems.Add(noticia.DataStr);
                item.SubItems.Add(noticia.Titulo);
                item.SubItems.Add(noticia.Link);
            }

            using (var db = new LiteDB.LiteDatabase(banco))
            {
                var col = db.GetCollection<Noticia>();
                col.Delete(LiteDB.Query.All());
                col.InsertBulk(noticias);
            }

            if (notificarBalao) {
                notifyIcon.ShowBalloonTip(5000);
                ExibirForm();
            }
        }

        private void ExibirForm()
        {
            Visible = true;
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            BringToFront();
        }

        private void btnBuscar_Click(object sender, EventArgs e)
        {
            Atualizar();
        }

        private void listView_DoubleClick(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 0)
                return;

            Noticia noticia = noticias[listView.SelectedItems[0].Index];

            SHELLEXECUTEINFO info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = "open";
            info.lpFile = noticia.Link;
            info.nShow = 3;
            info.lpClass = "http";
            ShellExecuteEx(ref info);
            //MessageBox.Show(noticia.Link, "Oops...", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void notifyIcon_Click(object sender, EventArgs e)
        {
            ExibirForm();
        }

        private void notifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            ExibirForm();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            bool mostrar = (WindowState == FormWindowState.Minimized);

            if (mostrar == notifyIcon.Visible)
                return;

            notifyIcon.Visible = mostrar;
            Visible = !mostrar;            

        }

        private void timer_Tick(object sender, EventArgs e)
        {
            Atualizar();
        }
    }
}
