using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace e_money
{
    class DeviceCardReader
    {
        int retCode;
        int hCard;
        int hContext;
        int Protocol;
        public bool connActive = false;
        string readername = "ACS ACR123U";      // change depending on reader
        public byte[] SendBuff = new byte[263];
        public byte[] RecvBuff = new byte[263];
        public int SendLen, RecvLen, nBytesRet, reqType, Aprotocol, dwProtocol, cbPciLength;
        public Card.SCARD_READERSTATE RdrState;
        public Card.SCARD_IO_REQUEST pioSendRequest;
        private BackgroundWorker worker;
        private Card.SCARD_READERSTATE[] states;

        internal enum SmartcardState
        {
            None = 0,
            Inserted = 1,
            Ejected = 2
        }

        public DeviceCardReader()
        {
            SelectDevice();
            establishContext();
        }

        public void SelectDevice()
        {
            List<string> availableReaders = this.ListReaders();
            if (availableReaders.Count == 0)
            {
                Console.WriteLine("No Reader Device found.");
                return;
            }
            this.RdrState = new Card.SCARD_READERSTATE();
            readername = availableReaders[0].ToString();//selecting first device
            Console.WriteLine("=========================================");
            Console.WriteLine("Device Name = " + readername);
            Console.WriteLine("=========================================");
            this.RdrState.RdrName = readername;

            states = new Card.SCARD_READERSTATE[1];
            states[0] = new Card.SCARD_READERSTATE();
            states[0].RdrName = readername;
            states[0].UserData = (int)IntPtr.Zero;
            states[0].RdrCurrState = Card.SCARD_STATE_EMPTY;
            states[0].RdrEventState = 0;
            states[0].ATRLength = 0;
            states[0].ATRValue = null;

            if (availableReaders.Count > 0)
            {
                this.worker = new BackgroundWorker();
                this.worker.WorkerSupportsCancellation = true;
                this.worker.DoWork += WaitChangeStatus;
                this.worker.RunWorkerAsync();
            }
        }

        private void WaitChangeStatus(object sender, DoWorkEventArgs e)
        {
            while (!e.Cancel)
            {
                int nErrCode = Card.SCardGetStatusChange(hContext, 1000, ref states[0], 1);
                //Check if the state changed from the last time.
                if ((this.states[0].RdrEventState & 2) == 2)
                {
                    //Check what changed.
                    SmartcardState state = SmartcardState.None;
                    if ((this.states[0].RdrEventState & 32) == 32 && (this.states[0].RdrCurrState & 32) != 32)
                    {
                        //The card was inserted. 
                        state = SmartcardState.Inserted;
                    }
                    else if ((this.states[0].RdrEventState & 16) == 16 && (this.states[0].RdrCurrState & 16) != 16)
                    {
                        //The card was ejected.
                        state = SmartcardState.Ejected;
                    }
                    if (state != SmartcardState.None && this.states[0].RdrCurrState != 0)
                    {
                        switch (state)
                        {
                            case SmartcardState.Inserted:
                                {
                                    //Console.WriteLine("Card inserted");
                                    RunMain();
                                    break;
                                }
                            case SmartcardState.Ejected:
                                {
                                    Console.WriteLine("\nCard ejected\n");
                                    break;
                                }
                            default:
                                {
                                    Console.WriteLine("Some other state...");
                                    break;
                                }
                        }
                    }
                    //Update the current state for the next time they are checked.
                    this.states[0].RdrCurrState = this.states[0].RdrEventState;
                }
            }
        }

        public List<string> ListReaders()
        {
            int ReaderCount = 0;
            List<string> AvailableReaderList = new List<string>();

            //Make sure a context has been established before 
            //retrieving the list of smartcard readers.
            retCode = Card.SCardListReaders(hContext, null, null, ref ReaderCount);
            if (retCode != Card.SCARD_S_SUCCESS)
            {
                Console.WriteLine(Card.GetScardErrMsg(retCode));
                //connActive = false;
            }

            byte[] ReadersList = new byte[ReaderCount];

            //Get the list of reader present again but this time add sReaderGroup, retData as 2rd & 3rd parameter respectively.
            retCode = Card.SCardListReaders(hContext, null, ReadersList, ref ReaderCount);
            if (retCode != Card.SCARD_S_SUCCESS)
            {
                Console.WriteLine(Card.GetScardErrMsg(retCode));
            }

            string rName = "";
            int indx = 0;
            if (ReaderCount > 0)
            {
                // Convert reader buffer to string
                while (ReadersList[indx] != 0)
                {

                    while (ReadersList[indx] != 0)
                    {
                        rName = rName + (char)ReadersList[indx];
                        indx = indx + 1;
                    }

                    //Add reader name to list
                    AvailableReaderList.Add(rName);
                    rName = "";
                    indx = indx + 1;

                }
            }
            return AvailableReaderList;

        }

        internal void establishContext()
        {
            retCode = Card.SCardEstablishContext(Card.SCARD_SCOPE_SYSTEM, 0, 0, ref hContext);
            if (retCode != Card.SCARD_S_SUCCESS)
            {
                Console.WriteLine("Check your device and please restart again", "Reader not connected");
                connActive = false;
                return;
            }
        }

        public bool connectCard()
        {
            connActive = true;

            retCode = Card.SCardConnect(hContext, readername, Card.SCARD_SHARE_SHARED,
                      Card.SCARD_PROTOCOL_T0 | Card.SCARD_PROTOCOL_T1, ref hCard, ref Protocol);

            if (retCode != Card.SCARD_S_SUCCESS)
            {
                Console.WriteLine("Card not available");
                connActive = false;
                return false;
            }
            return true;
        }

        private string getcardUID()//only for mifare 1k cards
        {
            string cardUID = "";
            byte[] receivedUID = new byte[256];
            Card.SCARD_IO_REQUEST request = new Card.SCARD_IO_REQUEST();
            request.dwProtocol = Card.SCARD_PROTOCOL_T1;
            request.cbPciLength = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Card.SCARD_IO_REQUEST));
            byte[] sendBytes = new byte[] { 0xFF, 0xCA, 0x00, 0x00, 0x00 }; //get UID command      for Mifare cards
            int outBytes = receivedUID.Length;
            int status = Card.SCardTransmit(hCard, ref request, ref sendBytes[0], sendBytes.Length, ref request, ref receivedUID[0], ref outBytes);
            if (status != Card.SCARD_S_SUCCESS)
            {
                cardUID = "Error";
            }
            else
            {
                cardUID = BitConverter.ToUInt32(receivedUID.Take(4).ToArray(), 0).ToString();

            }
            return cardUID;
        }

        // block authentication
        private bool FirstAuthentication()
        {
            ClearBuffers();
            SendBuff[0] = 0xFF;
            SendBuff[1] = 0x82;
            SendBuff[2] = 0x00;
            SendBuff[3] = 0x20;
            SendBuff[4] = 0x06;
            SendBuff[5] = 0xFF;
            SendBuff[6] = 0xFF;
            SendBuff[7] = 0xFF;
            SendBuff[8] = 0xFF;
            SendBuff[9] = 0xFF;
            SendBuff[10] = 0xFF;

            SendLen = 0x0B;
            RecvLen = 0x02;

            retCode = SendAPDUandDisplay(0);
            if (retCode != Card.SCARD_S_SUCCESS)
            {
                Console.WriteLine("First Authentication Failed!");
                return false;
            }
            Console.WriteLine("First Authentication Success!");
            return true;
        }

        private bool SecondAuthentication(string Block)
        {
            ClearBuffers();
            SendBuff[0] = 0xFF; // CLA 
            SendBuff[1] = 0x88;// INS
            SendBuff[2] = 0x00;// P1
            SendBuff[3] = (byte)int.Parse(Block);// P2 : Block No.
            SendBuff[4] = 0x60;// Le
            SendBuff[5] = 0x20;

            SendLen = 0x06;
            RecvLen = 0x02;

            retCode = SendAPDUandDisplay(0);

            if (retCode == -200)
            {
                //return "outofrangeexception";
                Console.WriteLine("2nd Authentication Failed!\n     Error Message : Out of Range Exception.");
                return false;
            }

            if (retCode == -202)
            {
                //return "BytesNotAcceptable";
                Console.WriteLine("2nd Authentication Failed!\n     Error Message : Bytes Not Acceptable.");
                return false;
            }

            if (retCode != Card.SCARD_S_SUCCESS)
            {
                //return "FailRead";
                Console.WriteLine("2nd Authentication Failed!\n     Error Message : Fail to Read.");
                return false;
            }
            Console.WriteLine("2nd Authentication Success!");
            return true;
        }

        // clear memory buffers
        private void ClearBuffers()
        {
            long indx;

            for (indx = 0; indx <= 262; indx++)
            {
                RecvBuff[indx] = 0;
                SendBuff[indx] = 0;
            }
        }

        // send application protocol data unit : communication unit between a smart card reader and a smart card
        private int SendAPDUandDisplay(int reqType)
        {
            int indx;
            string tmpStr = "";

            pioSendRequest.dwProtocol = Aprotocol;
            pioSendRequest.cbPciLength = 8;

            //Display Apdu In
            for (indx = 0; indx <= SendLen - 1; indx++)
            {
                tmpStr = tmpStr + " " + string.Format("{0:X2}", SendBuff[indx]);
            }
            Console.WriteLine("upper : " + tmpStr);

            retCode = Card.SCardTransmit(hCard, ref pioSendRequest, ref SendBuff[0],
                                 SendLen, ref pioSendRequest, ref RecvBuff[0], ref RecvLen);
            if (retCode != Card.SCARD_S_SUCCESS)
            {
                return retCode;
            }

            else
            {
                try
                {
                    tmpStr = "";
                    switch (reqType)
                    {
                        case 0:
                            for (indx = (RecvLen - 2); indx <= (RecvLen - 1); indx++)
                            {
                                tmpStr = tmpStr + " " + string.Format("{0:X2}", RecvBuff[indx]);
                            }
                            Console.WriteLine("case 0 : " + tmpStr);
                            if ((tmpStr).Trim() != "90 00")
                            {
                                //MessageBox.Show("Return bytes are not acceptable.");
                                return -202;
                            }
                            break;

                        case 1:

                            for (indx = (RecvLen - 2); indx <= (RecvLen - 1); indx++)
                            {
                                tmpStr = tmpStr + string.Format("{0:X2}", RecvBuff[indx]);
                            }

                            if (tmpStr.Trim() != "90 00")
                            {
                                tmpStr = tmpStr + " " + string.Format("{0:X2}", RecvBuff[indx]);
                            }

                            else
                            {
                                tmpStr = "ATR : ";
                                for (indx = 0; indx <= (RecvLen - 3); indx++)
                                {
                                    tmpStr = tmpStr + " " + string.Format("{0:X2}", RecvBuff[indx]);
                                }
                            }
                            //Console.WriteLine("case 1 : " + tmpStr);
                            break;

                        case 2:
                            for (indx = 0; indx <= (RecvLen - 1); indx++)
                            {
                                tmpStr = tmpStr + " " + string.Format("{0:X2}", RecvBuff[indx]);
                            }
                            Console.WriteLine("case 2 : " + tmpStr);
                            break;
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    return -200;
                }
            }
            return retCode;
        }

        //disconnect card reader connection
        public void Close()
        {
            if (connActive)
            {
                retCode = Card.SCardDisconnect(hCard, Card.SCARD_UNPOWER_CARD);
            }
            //retCode = Card.SCardReleaseContext(hCard);
        }

        public string VerifyCard(String Block)
        {
            string value = "";
            if (connectCard())
            {
                value = readBlock(Block);
            }
            value = value.Split(new char[] { '\0' }, 2, StringSplitOptions.None)[0].ToString();
            return value;
        }

        public string readBlock(String Block)
        {
            string tmpStr = "";
            int indx;
            if (this.FirstAuthentication() && this.SecondAuthentication(Block))
            {
                ClearBuffers();
                SendBuff[0] = 0xFF; // CLA 
                SendBuff[1] = 0xB0;// INS
                SendBuff[2] = 0x00;// P1
                SendBuff[3] = (byte)int.Parse(Block);// P2 : Block No.
                SendBuff[4] = (byte)int.Parse("16");// Le

                SendLen = 5;
                RecvLen = SendBuff[4] + 2;

                retCode = SendAPDUandDisplay(2);

                if (retCode == -200)
                {
                    Console.WriteLine("3rd Authentication Failed!");
                    return "outofrangeexception";
                }

                if (retCode == -202)
                {
                    Console.WriteLine("3rd Authentication Failed!");
                    return "BytesNotAcceptable";
                }

                if (retCode != Card.SCARD_S_SUCCESS)
                {
                    Console.WriteLine("3rd Authentication Failed!");
                    return "Fail Read";
                }

                // Display data in text format
                for (indx = 0; indx <= RecvLen - 1; indx++)
                {
                    tmpStr = tmpStr + Convert.ToChar(RecvBuff[indx]);
                }
                Console.WriteLine("3rd Authentication Success!");
                return (tmpStr);
            }
            else
            {
                return "Fail Authentication";
            }
        }

        /*  submit data method
         *  forbidden block to write into : 0,3,7,11,15.
         */
        public void submitText(String Text, String Block)
        {

            String tmpStr = Text;
            int indx;
            if (this.FirstAuthentication() && this.SecondAuthentication(Block))
            {
                ClearBuffers();
                SendBuff[0] = 0xFF;                             // CLA
                SendBuff[1] = 0xD6;                             // INS
                SendBuff[2] = 0x00;                             // P1
                SendBuff[3] = (byte)int.Parse(Block);           // P2 : Starting Block No.
                SendBuff[4] = (byte)int.Parse("16");            // P3 : Data length

                for (indx = 0; indx <= (tmpStr).Length - 1; indx++)
                {
                    SendBuff[indx + 5] = (byte)tmpStr[indx];
                }
                SendLen = SendBuff[4] + 5;
                RecvLen = 0x02;

                retCode = SendAPDUandDisplay(2);

                if (retCode != Card.SCARD_S_SUCCESS)
                {
                    Console.WriteLine("3rd Authentication Failed.   Error Message : fail to write.");

                }
                else
                {
                    Console.WriteLine("3rd Authentication : write success.");
                }
            }
            else
            {
                Console.WriteLine("Fail Authentication");
            }
        }

        private string GetIPv4()
        {
            string localIP = null;
            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIP = endPoint.Address.ToString();
                }
            }
            return localIP;
        }

        private string GetCurrentDatetime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss");
        }

        public void RunMain()
        {
            if (this.connectCard())
            {
                string cardUID = this.getcardUID();
                Console.WriteLine("Card UID : " + cardUID + "\n");

                //string current_dt = this.GetCurrentDatetime();
                //string[] temp = current_dt.Split(' ');
                //submitText(temp[0], "4"); // Date
                //submitText(temp[1], "5"); // time
                //submitText(this.GetIPv4(), "6"); // IP Address
                //Close();

                //Console.WriteLine("\nReading Result\n");
                //string text = VerifyCard("0");
                //Console.WriteLine("Block 0 : " + text.ToString() + "\n");
                string text = VerifyCard("4");
                Console.WriteLine("Block 4 : " + text.ToString() + "\n");

                string text2 = VerifyCard("5");
                Console.WriteLine("Block 5 : " + text2.ToString() + "\n");

                string text3 = VerifyCard("6");
                Console.WriteLine("Block 6 : " + text3.ToString());
            }
        }
    }
}
