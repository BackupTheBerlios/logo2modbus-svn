/* Copyright (c) 2008 Tobias Mache
 * 
 * This file is part of Logo2Modbus.
 * 
 * Logo2Modbus is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * Logo2Modbus is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with Logo2Modbus.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Collections;
using System.IO.Ports;
using Modbus.Device;
using System.Net.Sockets;
using System.Net;

namespace Logo2Modbus
{
    public partial class Form1 : Form
    {
        private LogoDriver logoDriver;
        private ModbusSlave modbusSlave;
        private TcpListener slaveTcpListener;

        public Form1()
        {
            InitializeComponent();
            addressBox.Items.AddRange(getIPAddresses());
            comPortBox.Items.AddRange(SerialPort.GetPortNames());
            logoDriver = new LogoDriver();
            logoDriver.logoStatusChanged += new LogoStatusChangeHandler(logostatusChanged);
            toolStripStatusLabel1.Visible = false;
        }

        private String[] getIPAddresses()
        {
            ArrayList res = new ArrayList();

            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface nic in nics)
            {
                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    res.Add(address.Address.ToString());
                }
            }
            String[] adds = new String[res.Count];
            res.CopyTo(adds);
            return adds;
        }

        private void onlyLocal_checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (onlyLocal_checkBox1.Checked)
            {
                addressBox.Enabled = false;
            }
            else
            {
                addressBox.Enabled = true;
            }
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            if (logoDriver.status == LogoStatus.Offline)
            {
                IPAddress address;
                if (onlyLocal_checkBox1.Checked)
                    address = new IPAddress(new byte[] { 127, 0, 0, 1 });
                else
                {
                    try
                    {
                        address = IPAddress.Parse((String)addressBox.SelectedItem);
                    }
                    catch (ArgumentNullException ex) { address = new IPAddress(new byte[] { 127, 0, 0, 1 }); }
                }
                if (!logoDriver.start()) MessageBox.Show("Der Ausgewählte Com-Port ist belegt.");
                else
                {
                    slaveTcpListener = new TcpListener(address, int.Parse(portBox.Text));
                    slaveTcpListener.Start();

                    modbusSlave = ModbusTcpSlave.CreateTcp(1, slaveTcpListener);
                    modbusSlave.DataStore = logoDriver.dataImage;

                    modbusSlave.Listen();
                    startButton.Text = "Trennen";
                }
            }
            else
            {
                logoDriver.stop();
                startButton.Text = "Verbinden";
                slaveTcpListener.Stop();
            }
        }

        private void comPortBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            logoDriver.setPortName((String) comPortBox.SelectedItem);
        }

        public void logostatusChanged()
        {
            if (this.InvokeRequired)
            {
                LogoStatusChangeHandler h = new LogoStatusChangeHandler(logostatusChanged);
                this.Invoke(h);
            }
            else
            {
                switch (logoDriver.status)
                {
                    case LogoStatus.Offline:
                        toolStripStatusLabel1.Visible = false;
                        groupBox1.Enabled = true;
                        comPortBox.Enabled = true;
                        break;
                    case LogoStatus.Online:
                        toolStripStatusLabel1.Text = "Offline";
                        toolStripStatusLabel1.Image = Logo2Modbus.Properties.Resources.Critical;
                        toolStripStatusLabel1.Visible = true;
                        groupBox1.Enabled = false;
                        comPortBox.Enabled = false;
                        break;
                    case LogoStatus.InStop:
                        toolStripStatusLabel1.Text = "Logo in Stop?";
                        toolStripStatusLabel1.Image = Logo2Modbus.Properties.Resources.Warning;
                        toolStripStatusLabel1.Visible = true;
                        groupBox1.Enabled = false;
                        comPortBox.Enabled = false;
                        break;
                    case LogoStatus.Connected:
                        toolStripStatusLabel1.Text = "Online";
                        toolStripStatusLabel1.Image = Logo2Modbus.Properties.Resources.OK;
                        toolStripStatusLabel1.Visible = true;
                        groupBox1.Enabled = false;
                        comPortBox.Enabled = false;
                        break;
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            logoDriver.stop();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}