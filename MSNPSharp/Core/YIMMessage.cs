#region Copyright (c) 2002-2008, Bas Geertsema, Xih Solutions (http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice
/*
Copyright (c) 2002-2008, Bas Geertsema, Xih Solutions
(http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice.
All rights reserved. http://code.google.com/p/msnp-sharp/

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice,
  this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* Neither the names of Bas Geertsema or Xih Solutions nor the names of its
  contributors may be used to endorse or promote products derived from this
  software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS 'AS IS'
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE. 
*/
#endregion

namespace MSNPSharp.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Collections;
    using System.Globalization;

    /// <summary>
    /// Yahoo Messenger Message
    /// </summary>
    [Serializable()]
    public class YIMMessage : MSNMessage
    {
        string _user = "";
        string _msgtype = "1";

        public YIMMessage(NSMessage message)
            : base("UBM", (ArrayList)message.CommandValues.Clone())
        {
            _user = message.CommandValues[0].ToString();
            _msgtype = message.CommandValues[2].ToString();

            Command = "UBM";
            //string strmessage = Encoding.UTF8.GetString(((NetworkMessage)message).InnerBody);
            //strmessage = strmessage.Substring(strmessage.IndexOf("\r\n") + 2);
            message.Command = "";
            message.CommandValues.Clear();
            //message.InnerBody = Encoding.UTF8.GetBytes(strmessage);
            //message.PrepareMessage();
            InnerMessage = new MSGMessage(message);
            InnerBody = GetBytes();
        }

        public YIMMessage(string command, string[] commandValues)
            : base(command, new ArrayList(commandValues))
        {
            _user = commandValues[0];
            _msgtype = commandValues[2];
        }

        public YIMMessage(string command, ArrayList commandValues)
            : base(command, commandValues)
        {
            _user = commandValues[0].ToString();
            _msgtype = commandValues[2].ToString();
        }

        public override byte[] GetBytes()
        {
            byte[] contents = null;

            //FIXME: maybe move this to SBMessage?
            if (InnerMessage != null)
            {
                contents = InnerMessage.GetBytes();

                // prepare a default MSG message if an inner message is specified
                if (CommandValues.Count == 0)
                {
                    if (Command != "UBM")
                        CommandValues.Add(TransactionID.ToString());

                    CommandValues.Add(_user);
                    CommandValues.Add("32");
                    CommandValues.Add(_msgtype);
                    CommandValues.Add(contents.Length.ToString(CultureInfo.InvariantCulture));
                }
            }

            StringBuilder builder = new StringBuilder(128);
            builder.Append(Command);

            if (CommandValues.Count > 0)
            {
                foreach (string val in CommandValues)
                {
                    builder.Append(' ');
                    builder.Append(val);
                }

                builder.Append("\r\n");
            }

            if (InnerMessage != null)
                return AppendArray(System.Text.Encoding.UTF8.GetBytes(builder.ToString()), contents);
            else
                return System.Text.Encoding.UTF8.GetBytes(builder.ToString());

        }

        public override void PrepareMessage()
        {
            CommandValues.Clear();
            if (InnerMessage != null)
                InnerMessage.PrepareMessage();
        }

        public override string ToDebugString()
        {
            GetBytes();
            byte[] contents = null;

            //FIXME: maybe move this to SBMessage?
            if (InnerMessage != null)
            {
                contents = InnerMessage.GetBytes();
            }

            StringBuilder builder = new StringBuilder(128);
            builder.Append(Command);

            if (CommandValues.Count > 0)
            {
                foreach (string val in CommandValues)
                {
                    builder.Append(' ');
                    builder.Append(val);
                }

                builder.Append("\r\n");
            }

            if (InnerMessage != null)
                return System.Text.Encoding.UTF8.GetString(AppendArray(System.Text.Encoding.UTF8.GetBytes(builder.ToString()), contents));
            else
                return System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
        }

        public new ArrayList CommandValues
        {
            get
            {
                return base.CommandValues;
            }
            set
            {
                base.CommandValues = value;
            }
        }

        public new string Command
        {
            get
            {
                return base.Command;
            }
            set
            {
                base.Command = value;
            }
        }

        public new MSGMessage InnerMessage
        {
            get
            {
                return (MSGMessage)base.InnerMessage;
            }
            set
            {
                base.InnerMessage = value;
            }
        }

        public new int TransactionID
        {
            get
            {
                return base.TransactionID;
            }
            set
            {
                base.TransactionID = value;
            }
        }
    }
};