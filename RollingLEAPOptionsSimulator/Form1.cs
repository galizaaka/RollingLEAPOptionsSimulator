﻿
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ZedGraph;
using System;
using System.Runtime.InteropServices;
using RollingLEAPOptionsSimulator.Models;
using RollingLEAPOptionsSimulator.Utility;

namespace RollingLEAPOptionsSimulator
{
    public partial class Form1 : Form, Handler
    {
        public AmeritradeClient TDAmeritradeClient;
        private Excel.Workbook _workbook;
        private Excel.Application _xlApp;
        private Excel._Worksheet _mainWorksheet;
        private Excel._Worksheet _pnlSheet;


        private string SourceIDKey = "SourceID";
        private string FilePathKey = "FilePath";
        string path;

        private Dictionary<string, List<OptionQuote>> options;
        private List<StockQuote> quotes;

        public Form1()
        {
            TDAmeritradeClient = new AmeritradeClient();
            InitializeComponent();
            info("Starting application...");          
            path = Settings.GetProtected(FilePathKey);
            fileLabel.Text = path;

        }

        void ThisWorkbook_BeforeClose(ref bool Cancel)
        {
            this.Invoke((MethodInvoker)delegate
            {
                cancelButton.PerformClick();
            });
        }

        public void info(string v)
        {
            error(v, null);
        }

        public void error(string text, Exception ex)
        {
            string timestamp = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss - ");
            string threadId = "Thread " + Thread.CurrentThread.ManagedThreadId + ": ";

            if (InvokeRequired)
            {
                text = timestamp + threadId + text;
                this.Invoke(new Action<string, Exception>(error), new object[] { text, ex });
                return;
            }
            else if (!text.Contains("Thread"))
            {
                text = timestamp + threadId + text;
            }

            if (ex != null)
            {
                text += "\r\n" + ex.Message;
                text += "\r\n" + ex.StackTrace;
            }

            output.AppendText(text + "\r\n");
        }

        private void login_Click(object sender, EventArgs e)
        {
            login();
        }

        private void login()
        {           
            Settings.SetProtected(FilePathKey, path);

            if (GetWorkBook() == null)
            {
                error("Unable to open workbook. Please select a correct Excel template file.", null);
                return;
            }

            
            GetMainWorkSheet().Select();
            GetExcel().Visible = true;

            Refresh();                      
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Refresh();
        }

        public override void Refresh()
        {           
                try
                {
                    RefreshStocks();
                }
                catch (Exception ex)
                {
                    error("Unable to refresh stock quotes", ex);
                }

                try
                {
                    RefreshOptions();

                }
                catch (Exception ex)
                {
                    error("Unable to refresh options", ex);
                }               
        }


        private void RefreshStocks()
        {
            lock (excelLock)
            {
                info("Locked Excel.");
                info("Refreshing stock quotes...");
                GetMainWorkSheet().Range["N2:P8"].Font.Color = ColorTranslator.ToOle(Color.Red);
                List<String> symbols = new List<string>() ;

                for (int row = 2; row < 9; row++)
                {
                    string symbol = (string)(GetMainWorkSheet().Cells[row, 3] as Excel.Range).Value;
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        symbols.Add(symbol);
                    }
                }

                GetStockQuote getStockQuote = new GetStockQuote(TDAmeritradeClient, this, symbols.ToArray());
                Thread oThread = new Thread(new ThreadStart(getStockQuote.GetQuotes));
                oThread.Start();

            }
            info("Unlocked Excel.");

        }




        private Excel.Application GetExcel()
        {
            if (_xlApp == null)
            {
                _xlApp = new Excel.Application();
            }
            return _xlApp;
        }

        private Excel.Workbook GetWorkBook()
        {
            if (_workbook == null)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    _workbook = GetExcel().Workbooks.Open(path);
                    _workbook.BeforeClose += ThisWorkbook_BeforeClose;
                }
            }
            return _workbook;
        }

        private Excel._Worksheet GetMainWorkSheet()
        {
            if (_mainWorksheet == null)
            {
                _mainWorksheet = (Excel._Worksheet)GetWorkBook().Sheets["Main"];
            }
            return _mainWorksheet;
        }

        private Excel._Worksheet GetPnLWorksheetSheet()
        {
            if (_pnlSheet == null)
            {
                _pnlSheet = (Excel._Worksheet)GetWorkBook().Sheets["PNL"];
            }
            return _pnlSheet;
        }


        private void RefreshOptions()
        {

            lock (excelLock)
            {
                info("Locked Excel.");
                info("Refreshing option quotes...");
                GetMainWorkSheet().Range["C2:C8"].Font.Color = ColorTranslator.ToOle(Color.Red);

                for (int row = 2; row < 9; row++)
                {
                    string symbol = (string)(GetMainWorkSheet().Cells[row, 3] as Excel.Range).Value;
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        GetoptionChain getoptionChain = new GetoptionChain(symbol, TDAmeritradeClient, this);
                        Thread oThread = new Thread(new ThreadStart(getoptionChain.GetOptionChain));
                        oThread.Start();
                    }
                }
            }
            info("Unlocked Excel.");
        }

 

        private void Cleanup()
        {
            //cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();

            //rule of thumb for releasing com objects:
            //  never use two dots, all COM objects must be referenced and released individually
            //  ex: [somthing].[something].[something] is bad

            //release com objects to fully kill excel process from running in the background
            if (_mainWorksheet != null)
            {
                Marshal.ReleaseComObject(_mainWorksheet);
            }


            //close and release
            //  xlWorkbook.Save();
            if (_workbook != null)
            {
                Marshal.ReleaseComObject(_workbook);
            }


            //quit and release
            if (_xlApp != null)
            {
                _xlApp.Quit();
                Marshal.ReleaseComObject(_xlApp);
            }
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Cleanup();
        }

        private object excelLock = new object();


        void Handler.HandleOptionChain(List<object> options)
        {
            if (options.Count > 0)
            {
                lock (excelLock)
                {
                    info("Locked Excel.");
                    try
                    {
                        string symbol = (options[0] as OptionStrike).Call.UnderlyingSymbol;
                        Excel._Worksheet symbolSheet = (Excel._Worksheet)GetWorkBook().Sheets[symbol];
                        Excel.Range xlRange = symbolSheet.UsedRange;
                        xlRange.ClearContents();
                        object[,] data = new object[options.Count, 13];
                        int row = 0;
                        foreach (OptionStrike optionStrike in options)
                        {

                            Call call = optionStrike.Call;

                            data[row, 0] = call.Symbol;
                            data[row, 1] = call.GetType().Name;
                            data[row, 3] = optionStrike.ExpirationDate.ToString("yyyy-MM-dd");
                            data[row, 5] = optionStrike.StrikePrice;
                            data[row, 9] = call.Bid;
                            data[row, 10] = call.Ask;
                          //  data[row, 11] = option.ExpirationType;
                           // data[row, 12] = call.Delta;
                            row++;
                        }
                        xlRange = GetExcel().Range[symbolSheet.Cells[1, 1], symbolSheet.Cells[data.GetLength(0), data.GetLength(1)]];
                        xlRange.Value = data;
                        (GetMainWorkSheet().Cells[25, "C"] as Excel.Range).Value = DateTime.Today;
                        (GetMainWorkSheet().Cells[GetSymbolRow(symbol), "C"] as Excel.Range).Font.Color = ColorTranslator.ToOle(Color.Black);
                    }
                    catch (Exception ex)
                    {
                        error("Unable to handle option chain", ex);
                    }

                }
                info("Unlocked Excel.");

            }
        }

        private int GetSymbolRow(string symbol)
        {
            for (int row = 2; row < 9; row++)
            {
                var range = (GetMainWorkSheet().Cells[row, 3] as Excel.Range);
                string cell = (string)range.Value;
                if (!string.IsNullOrEmpty(cell) && cell.Equals(symbol))
                {
                    return row;
                }
            }
            return 0;
        }

        public void HandleStockQuote(List<object> quotes)
        {
            lock (excelLock)
            {
                info("Locked Excel.");
                try
                {
                    foreach(StockQuote quote in quotes)
                    {
                        object[] data = new object[3];
                        data[0] = quote.Last;
                        data[1] = quote.Change;
                        data[2] = quote.Close;
                        int row = GetSymbolRow(quote.Symbol);
                        Excel.Range xlRange = GetExcel().Range[GetMainWorkSheet().Cells[row, 14], GetMainWorkSheet().Cells[row, 16]];
                        xlRange.Value = data;
                        (GetMainWorkSheet().Cells[row, 14] as Excel.Range).Font.Color = ColorTranslator.ToOle(Color.Black);
                        (GetMainWorkSheet().Cells[row, 15] as Excel.Range).Font.Color = ColorTranslator.ToOle(Color.Black);
                        (GetMainWorkSheet().Cells[row, 16] as Excel.Range).Font.Color = ColorTranslator.ToOle(Color.Black);

                    }
                   
                }
                catch (Exception ex)
                {
                    error("Unable to hendle stock quote", ex);
                }

            }
            info("Unlocked Excel.");
        }



        private void button3_Click(object sender, EventArgs e)
        {

            OpenFileDialog file = new OpenFileDialog();
            if (file.ShowDialog() == DialogResult.OK)
            {
                path = file.FileName;
                fileLabel.Text = path;
            }
        }

        private void fileLabel_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {


        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(path))
            {
                login();
            }

        }
    }


    public class GetStockQuote
    {
        private string[] symbols;
        public AmeritradeClient oBroker;
        private Handler handler;

        public GetStockQuote(AmeritradeClient oBroker,
           Handler handler, params string[] symbols)
        {
            this.symbols = symbols;
            this.oBroker = oBroker;
            this.handler = handler;
        }


        public async void GetQuotes()
        {
          
            var quotes = await oBroker.GetQuotes(symbols);
            handler.HandleStockQuote(quotes);
           
        }
    }

    public interface Handler
    {
        void HandleOptionChain(List<object> options);
        void HandleStockQuote(List<object> quotes);

        void info(string v);
        void error(string text, Exception ex);
    }


    public class GetoptionChain
    {
        private string symbol;
        private string source;
        private string password;
        private string username;
        public AmeritradeClient oBroker;
        public Handler handler;

        public GetoptionChain(string symbol, AmeritradeClient oBroker,
           Handler handler)
        {
            this.symbol = symbol;
            this.oBroker = oBroker;
            this.handler = handler;
        }


        public async void GetOptionChain()
        {
            List<object> options = await oBroker.GetOptionChain(symbol);
            handler.HandleOptionChain(options);
        }
    }
}
