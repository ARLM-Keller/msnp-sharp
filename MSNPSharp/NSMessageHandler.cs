﻿#region Copyright (c) 2002-2008, Bas Geertsema, Xih Solutions (http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice
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

#define TRACE

using System;
using System.IO;
using System.Net;
using System.Web;
using System.Xml;
using System.Text;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MSNPSharp
{
    using MSNPSharp.Core;

    /// <summary>
    /// Handles the protocol messages from the notification server
    /// and implements protocol version between MSNP15 and MSNP16.
    /// </summary>
    public partial class NSMessageHandler : IMessageHandler
    {
        public static readonly Guid MachineGuid = Guid.NewGuid();

        #region Members

        private SocketMessageProcessor messageProcessor;
        private ConnectivitySettings connectivitySettings;
        private IPEndPoint externalEndPoint;
        private Credentials credentials;

        private ContactGroupList contactGroups;
        private ContactList contactList = new ContactList();
        private bool autoSynchronize = true;

        private bool isSignedIn;
        private Owner owner = new Owner();
        private MSNTicket msnticket = MSNTicket.Empty;
        private Queue pendingSwitchboards = new Queue();

        private ContactService contactService;
        private OIMService oimService;
        private ContactSpaceService spaceService;
        private MSNStorageService storageService;

        private List<Regex> censorWords = new List<Regex>(0);

        private NSMessageHandler()
        {
            owner.NSMessageHandler = this;
#if MSNC9
            owner.ClientCapacities = ClientCapacities.CanHandleMSNC9
#else
            owner.ClientCapacities = ClientCapacities.CanHandleMSNC8
#endif

 | ClientCapacities.CanMultiPacketMSG | ClientCapacities.CanReceiveWinks;

            contactGroups = new ContactGroupList(this);
            contactService = new ContactService(this);
            oimService = new OIMService(this);
            spaceService = new ContactSpaceService(this);
            storageService = new MSNStorageService(this);
        }

        #endregion

        #region Properties

        public bool AutoSynchronize
        {
            get
            {
                return autoSynchronize;
            }
            set
            {
                autoSynchronize = value;
            }
        }

        /// <summary>
        /// Defines whether the user is signed in the messenger network
        /// </summary>
        public bool IsSignedIn
        {
            get
            {
                return isSignedIn;
            }
        }

        /// <summary>
        /// Keep track whether a address book synchronization has been completed so we can warn the client programmer
        /// </summary>
        public bool AddressBookSynchronized
        {
            get
            {
                return ContactService.AddressBookSynchronized;
            }
        }

        /// <summary>
        /// The end point as perceived by the server. This is set after the owner's profile is received.
        /// </summary>
        public IPEndPoint ExternalEndPoint
        {
            get
            {
                return externalEndPoint;
            }
        }

        /// <summary>
        /// A collection of all contacts which are on any of the lists of the person who logged into the messenger network
        /// </summary>
        public ContactList ContactList
        {
            get
            {
                return contactList;
            }
        }

        /// <summary>
        /// Censors that cannot contain in text messages.
        /// </summary>
        public List<Regex> CensorWords
        {
            get
            {
                return censorWords;
            }
        }

        /// <summary>
        /// The owner of the contactlist. This is the identity that logged into the messenger network.
        /// </summary>
        public Owner Owner
        {
            get
            {
                return owner;
            }
        }

        /// <summary>
        /// A collection of all contactgroups which are defined by the user who logged into the messenger network.\
        /// </summary>
        public ContactGroupList ContactGroups
        {
            get
            {
                return contactGroups;
            }
        }

        /// <summary>
        /// These credentials are used for user authentication and client identification
        /// </summary>
        public Credentials Credentials
        {
            get
            {
                return credentials;
            }
            set
            {
                credentials = value;
            }
        }

        /// <summary>
        /// If WebProxy is set the Webproxy is used for the
        /// authentication with Passport.com
        /// </summary>
        public ConnectivitySettings ConnectivitySettings
        {
            get
            {
                return connectivitySettings;
            }
            set
            {
                connectivitySettings = value;
            }
        }

        /// <summary>
        /// A service that provide contact operations.
        /// </summary>
        public ContactService ContactService
        {
            get
            {
                return contactService;
            }
        }

        /// <summary>
        /// Offline message service.
        /// </summary>
        public OIMService OIMService
        {
            get
            {
                return oimService;
            }
        }

        /// <summary>
        /// Contactcard service.
        /// </summary>
        public ContactSpaceService SpaceService
        {
            get
            {
                return spaceService;
            }
        }

        /// <summary>
        /// Storage Service for get/update display name, personal status, display picture etc.
        /// </summary>
        public MSNStorageService StorageService
        {
            get
            {
                return storageService;
            }
        }

        /// <summary>
        /// The processor to handle the messages
        /// </summary>
        public IMessageProcessor MessageProcessor
        {
            get
            {
                return messageProcessor;
            }
            set
            {
                if (processorConnectedHandler != null && messageProcessor != null)
                {
                    // de-register from the previous message processor					
                    ((SocketMessageProcessor)messageProcessor).ConnectionEstablished -= processorConnectedHandler;
                }

                if (processorConnectedHandler == null)
                {
                    processorConnectedHandler = new EventHandler<EventArgs>(NSMessageHandler_ProcessorConnectCallback);
                    processorDisconnectedHandler = new EventHandler<EventArgs>(NSMessageHandler_ProcessorDisconnectCallback);
                }

                messageProcessor = (SocketMessageProcessor)value;

                // catch the connect event so we can start sending the USR command upon initiating
                ((SocketMessageProcessor)messageProcessor).ConnectionEstablished += processorConnectedHandler;

                // and make sure we respond on closing
                ((SocketMessageProcessor)messageProcessor).ConnectionClosed += processorDisconnectedHandler;
            }
        }

        internal MSNTicket MSNTicket
        {
            get
            {
                return msnticket;
            }
            set
            {
                msnticket = value;
            }
        }

        #endregion

        #region Public Events

        /// <summary>
        /// Occurs when an exception is thrown while handling the incoming or outgoing messages
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ExceptionOccurred;

        /// <summary>
        /// Occurs when the user could not be signed in due to authentication errors. Most likely due to an invalid account or password. Note that this event will also raise the more general <see cref="ExceptionOccurred"/> event.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> AuthenticationError;

        /// <summary>
        /// Occurs when an answer is received after sending a ping to the MSN server via the SendPing() method
        /// </summary>
        public event EventHandler<PingAnswerEventArgs> PingAnswer;

        /// <summary>
        /// Occurs when any contact changes status
        /// </summary>
        public event EventHandler<ContactStatusChangeEventArgs> ContactStatusChanged;

        /// <summary>
        /// Occurs when any contact goes from offline status to another status
        /// </summary>
        public event EventHandler<ContactEventArgs> ContactOnline;

        /// <summary>
        /// Occurs when any contact goed from any status to offline status
        /// </summary>
        public event EventHandler<ContactEventArgs> ContactOffline;

        /// <summary>
        /// Occurs when the authentication and authorzation with the server has finished. The client is now connected to the messenger network.
        /// </summary>
        public event EventHandler<EventArgs> SignedIn;

        /// <summary>
        /// Occurs when the message processor has disconnected, and thus the user is no longer signed in.
        /// </summary>
        public event EventHandler<SignedOffEventArgs> SignedOff;

        /// <summary>
        /// Occurs when a switchboard session has been created
        /// </summary>
        public event EventHandler<SBCreatedEventArgs> SBCreated;

        /// <summary>
        /// Occurs when the server notifies the client with the status of the owner's mailbox.
        /// </summary>
        public event EventHandler<MailboxStatusEventArgs> MailboxStatusReceived;

        /// <summary>
        /// Occurs when new mail is received by the Owner.
        /// </summary>
        public event EventHandler<NewMailEventArgs> NewMailReceived;

        /// <summary>
        /// Occurs when unread mail is read or mail is moved by the Owner.
        /// </summary>
        public event EventHandler<MailChangedEventArgs> MailboxChanged;

        /// <summary>
        /// Occurs when the the server send an error.
        /// </summary>
        public event EventHandler<MSNErrorEventArgs> ServerErrorReceived;

        #endregion

        #region Public Methods

        #region Mobile

        /// <summary>
        /// Sets the telephonenumber for the contact list owner's homephone.
        /// </summary>
        public virtual void SetPhoneNumberHome(string number)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            if (number.Length > 30)
                throw new MSNPSharpException("Telephone number too long. Maximum length for a phone number is 30 digits.");

            MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "PHH", HttpUtility.UrlEncode(number) }));
        }

        /// <summary>
        /// Sets the telephonenumber for the contact list owner's workphone.
        /// </summary>
        public virtual void SetPhoneNumberWork(string number)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            if (number.Length > 30)
                throw new MSNPSharpException("Telephone number too long. Maximum length for a phone number is 30 digits.");

            MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "PHW", HttpUtility.UrlEncode(number) }));
        }

        /// <summary>
        /// Sets the telephonenumber for the contact list owner's mobile phone.
        /// </summary>
        public virtual void SetPhoneNumberMobile(string number)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            if (number.Length > 30)
                throw new MSNPSharpException("Telephone number too long. Maximum length for a phone number is 30 digits.");

            MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "PHM", HttpUtility.UrlEncode(number) }));
        }

        /// <summary>
        /// Sets whether the contact list owner allows remote contacts to send messages to it's mobile device.
        /// </summary>
        public virtual void SetMobileAccess(bool enabled)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "MOB", enabled ? "Y" : "N" }));
        }

        /// <summary>
        /// Sets whether the contact list owner has a mobile device enabled.
        /// </summary>
        public virtual void SetMobileDevice(bool enabled)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "MBE", enabled ? "Y" : "N" }));
        }

        /// <summary>
        /// Sends a mobile message to the specified remote contact. This only works when the remote contact has it's mobile device enabled and has MSN-direct enabled.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="text"></param>
        public virtual void SendMobileMessage(Contact receiver, string text)
        {
            SendMobileMessage(receiver, text, String.Empty, String.Empty);
        }

        /// <summary>
        /// Sends a mobile message to the specified remote contact. This only works when the remote contact has it's mobile device enabled and has MSN-direct enabled.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="text"></param>
        /// <param name="callbackNumber"></param>
        /// <param name="callbackDevice"></param>
        public virtual void SendMobileMessage(Contact receiver, string text, string callbackNumber, string callbackDevice)
        {
            if (receiver.MobileAccess || receiver.ClientType == ClientType.PhoneMember)
            {
#if MSNP18
                string to = (receiver.ClientType == ClientType.PhoneMember) ? "tel:" + receiver.Mail : receiver.Mail;
                string payload = "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=iso-8859-1\r\nDest-Agent: mobile\r\n\r\n" + text;
                NSPayLoadMessage nsMessage = new NSPayLoadMessage("UUM", new string[] { to, ((int)receiver.ClientType).ToString(), "1" }, payload);
                MessageProcessor.SendMessage(nsMessage);
#else
                // create a body message
                MobileMessage bodyMessage = new MobileMessage();
                bodyMessage.CallbackDeviceName = callbackDevice;
                bodyMessage.CallbackNumber = callbackNumber;
                bodyMessage.Receiver = receiver.Mail;
                bodyMessage.Text = text;

                // create a NSPayLoadMessage to transport it
                string to = (receiver.ClientType == ClientType.PhoneMember) ? "tel:" + receiver.Mail : receiver.Mail;
                NSPayLoadMessage nsMessage = new NSPayLoadMessage("PGD", new string[] { to, "1" }, Encoding.UTF8.GetString(bodyMessage.GetBytes()));

                // and send it
                MessageProcessor.SendMessage(nsMessage);

#endif
            }
            else
            {
                throw new MSNPSharpException("The specified contact has no mobile device enabled nor phone member.");
            }
        }


        #endregion

        #region RequestSwitchboard & SendPing

        internal List<SBMessageHandler> SwitchBoards = new List<SBMessageHandler>();

        /// <summary>
        /// Sends a request to the server to start a new switchboard session.
        /// </summary>
        public virtual SBMessageHandler RequestSwitchboard(object initiator)
        {
            // create a switchboard object
            SBMessageHandler handler = Factory.CreateSwitchboardHandler();

            RequestSwitchboard(handler, initiator);

            return handler;
        }

        /// <summary>
        /// Sends a request to the server to start a new switchboard session. The specified switchboard handler will be associated with the new switchboard session.
        /// </summary>
        /// <param name="switchboardHandler">The switchboard handler to use. A switchboard processor will be created and connected to this handler.</param>
        /// <param name="initiator">The object that initiated the request for the switchboard.</param>
        /// <returns></returns>
        public virtual void RequestSwitchboard(SBMessageHandler switchboardHandler, object initiator)
        {
            pendingSwitchboards.Enqueue(new SwitchboardQueueItem(switchboardHandler, initiator));
            MessageProcessor.SendMessage(new NSMessage("XFR", new string[] { "SB" }));
        }

        /// <summary>
        /// Sends PNG (ping) command.
        /// </summary>
        public virtual void SendPing()
        {
            MessageProcessor.SendMessage(new NSMessage("PNG"));
        }

        #endregion

        #region RequestScreenName & SetScreenName & SetPersonalMessage

        /// <summary>
        /// Send the server a request for the contact's screen name.
        /// </summary>
        /// <remarks>
        /// When the server replies with the screen name the Name property of the <see cref="Contact"/> will
        /// be updated.
        /// </remarks>
        /// <param name="contact"></param>
        public virtual void RequestScreenName(Contact contact)
        {
            if (contact.Guid == null || contact.Guid == Guid.Empty)
                throw new InvalidOperationException("This is not a valid Messenger contact.");

            MessageProcessor.SendMessage(new NSMessage("SBP", new string[] { contact.Guid.ToString(), "MFN", HttpUtility.UrlEncode(contact.Name).Replace("+", "%20") }));
        }

        /// <summary>
        /// Sets the contactlist owner's screenname. After receiving confirmation from the server
        /// this will set the Owner object's name which will in turn raise the NameChange event.
        /// </summary>
        public virtual void SetScreenName(string newName)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            if (owner.PassportVerified)
            {
                MessageProcessor.SendMessage(new NSMessage("PRP", new string[] { "MFN", HttpUtility.UrlEncode(newName).Replace("+", "%20") }));
            }
            if (AutoSynchronize)
            {
                StorageService.UpdateProfile(newName, Owner.PersonalMessage != null && Owner.PersonalMessage.Message != null ? Owner.PersonalMessage.Message : String.Empty);
            }
        }

        /// <summary>
        /// Sets personal message.
        /// </summary>
        /// <param name="pmsg"></param>
        public virtual void SetPersonalMessage(PersonalMessage pmsg)
        {
            if (owner == null)
                throw new MSNPSharpException("Not a valid owner");

            MessageProcessor.SendMessage(new NSPayLoadMessage("UUX", pmsg.Payload));

            if (AutoSynchronize)
            {
                StorageService.UpdateProfile(Owner.Name, pmsg.Message);
            }
        }

        #endregion

        #region SetPrivacyMode & SetNotifyPrivacyMode & SetPresenceStatus

        /// <summary>
        /// Set the contactlist owner's privacy mode.
        /// </summary>
        /// <param name="privacy">New privacy setting</param>
        public virtual void SetPrivacyMode(PrivacyMode privacy)
        {
            if (privacy == PrivacyMode.AllExceptBlocked)
                MessageProcessor.SendMessage(new NSMessage("BLP", new string[] { "AL" }));

            if (privacy == PrivacyMode.NoneButAllowed)
                MessageProcessor.SendMessage(new NSMessage("BLP", new string[] { "BL" }));

        }

        /// <summary>
        /// Set the contactlist owner's notification mode on contact service.
        /// </summary>
        /// <param name="privacy">New notify privacy setting</param>
        public virtual void SetNotifyPrivacyMode(NotifyPrivacy privacy)
        {
            Owner.SetNotifyPrivacy(privacy);
            if (AutoSynchronize)
            {
                ContactService.UpdateMe();
            }
        }

        /// <summary>
        /// Set the status of the contact list owner (the client).
        /// </summary>
        /// <remarks>You can only set the status _after_ SignedIn event. Otherwise you won't receive online notifications from other clients or the connection is closed by the server.</remarks>
        /// <param name="status"></param>
        public virtual void SetPresenceStatus(PresenceStatus status)
        {
            // check whether we are allowed to send a CHG command
            if (IsSignedIn == false)
                throw new MSNPSharpException("Can't set status. You must wait for the SignedIn event before you can set an initial status.");

            string context = String.Empty;

            if (owner.DisplayImage != null)
                context = owner.DisplayImage.Context;

            if (status == PresenceStatus.Offline)
                messageProcessor.Disconnect();

            //don't set the same status or it will result in disconnection
            else if (status != owner.Status)
            {
#if MSNP18
#if MSNC9
                string capacities = ((long)owner.ClientCapacities).ToString() + ":48";
#else
                string capacities = ((long)owner.ClientCapacities).ToString() + ":0";
#endif
#else
                string capacities = ((long)owner.ClientCapacities).ToString();
#endif
                MessageProcessor.SendMessage(new NSMessage("CHG", new string[] { ParseStatus(status), capacities, context }));
            }
        }

        #endregion

        #endregion

        #region Command Handlers

        #region Login

        /// <summary>
        /// Called when the message processor has disconnected.
        /// </summary>
        /// <param name="sender"></param>
        protected virtual void OnProcessorDisconnectCallback(IMessageProcessor sender)
        {
            // do nothing
        }

        /// <summary>
        /// Called when the message processor has established a connection. This function will 
        /// begin the login procedure by sending the VER command.
        /// </summary>
        /// <param name="sender"></param>
        protected virtual void OnProcessorConnectCallback(IMessageProcessor sender)
        {
            // Check for valid credentials
            if (Credentials == null)
                throw new MSNPSharpException("No Credentials passed in the NSMessageHandler");

            // VER: MSN Protocol used
#if MSNP16
            MessageProcessor.SendMessage(new NSMessage("VER", new string[] { "MSNP16", "MSNP15", "CVR0" }));
#else
            MessageProcessor.SendMessage(new NSMessage("VER", new string[] { "MSNP15", "CVR0" }));
#endif
            // CVR: Send client information back
            MessageProcessor.SendMessage(new NSMessage("CVR",
                new string[] { 
                    "0x040c", /*"0x" + CultureInfo.CurrentCulture.LCID.ToString("x4"), */  //The LCIDs in .net framework are different from Windows API.
                    "winnt", 
                    "5.1", 
                    "i386", 
                    Properties.Resources.MessengerClientName, 
                    Properties.Resources.MessengerClientBuildVer, 
                    Properties.Resources.MessengerClientBrand, 
                    Credentials.Account 
                }));

            // USR: Begin login procedure
            MessageProcessor.SendMessage(new NSMessage("USR", new string[] { "SSO", "I", Credentials.Account }));
        }

        /// <summary>
        /// Send the first message to the server.
        /// </summary>
        protected virtual void SendInitialMessage()
        {
        }

        /// <summary>
        /// Called when a VER command has been received. 
        /// </summary>
        /// <remarks>
        /// Indicates that the server has approved our version of the protocol. This function will send the CVR command.
        /// <code>VER [Transaction] [Protocol1] ([Protocol2]) [Clientversion]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnVERReceived(NSMessage message)
        {            
        }

        /// <summary>
        /// Called when a CVR command has been received. 
        /// </summary>
        /// <remarks>
        /// Indicates that the server has approved our client details. This function will send the USR command. 
        /// <code>CVR [Transaction] [Recommended version] [Recommended version] [Minimum version] [Download url] [Info url]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnCVRReceived(NSMessage message)
        {
        }

        /// <summary>
        /// Called when a USR command has been received. 
        /// </summary>
        /// <remarks>
        /// 
        /// <code>USR [Transaction] [SSO|OK] [Account] [Policy|Verified] [Nonce]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnUSRReceived(NSMessage message)
        {
            //single-sign-on stuff
            if ((string)message.CommandValues[1] == "SSO")
            {
                string policy = (string)message.CommandValues[3];
                string nonce = (string)message.CommandValues[4];

                try
                {
                    SingleSignOnManager.Authenticate(this, policy);
                }
                catch (Exception exception)
                {
                    OnAuthenticationErrorOccurred(new ExceptionEventArgs(exception));
                    return;
                }

                MBI mbi = new MBI();
                string response =
                    MSNTicket.SSOTickets[SSOTicketType.Clear].Ticket + " " +
                    mbi.Encrypt(MSNTicket.SSOTickets[SSOTicketType.Clear].BinarySecret, nonce);
#if MSNP16 || MSNP18
                MessageProcessor.SendMessage(new NSMessage("USR", new string[] { "SSO", "S", response, MachineGuid.ToString("B") }));
#else
                MessageProcessor.SendMessage(new NSMessage("USR", new string[] { "SSO", "S", response }));
#endif
            }
            else if ((string)message.CommandValues[1] == "OK")
            {
                // we sucesfully logged in, set the owner's name
                Owner.Mail = message.CommandValues[2].ToString();
                Owner.PassportVerified = message.CommandValues[3].Equals("1");
            }
        }

        /// <summary>
        /// Fires the <see cref="SignedIn"/> event.
        /// </summary>
        /// <param name="e"></param>
        protected internal virtual void OnSignedIn(EventArgs e)
        {
            isSignedIn = true;

            if (SignedIn != null)
                SignedIn(this, e);

            SendPing(); //Ping the server for the first time. Then client programmer should handle the answer.
        }

        /// <summary>
        /// Fires the <see cref="SignedOff"/> event.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnSignedOff(SignedOffEventArgs e)
        {
            owner.SetStatus(PresenceStatus.Offline);
            Clear();

            if (SignedOff != null)
                SignedOff(this, e);

            if (messageProcessor != null)
                messageProcessor.Disconnect();
        }


        #endregion

        #region Status


        /// <summary>
        /// Translates MSNStatus enumeration to messenger's textual status presentation.
        /// </summary>
        /// <param name="status">MSNStatus enum object representing the status to translate</param>
        /// <returns>The corresponding textual value</returns>
        internal protected static string ParseStatus(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.Online:
                    return "NLN";

                case PresenceStatus.Busy:
                case PresenceStatus.Phone:
                    return "BSY";

                case PresenceStatus.Idle:
                    return "IDL";

                case PresenceStatus.BRB:
                case PresenceStatus.Away:
                case PresenceStatus.Lunch:
                    return "AWY";

                case PresenceStatus.Offline:
                    return "FLN";

                case PresenceStatus.Hidden:
                    return "HDN";

                default:
                    break;
            }

            return "Unknown status";
        }

        /// <summary>
        /// Translates messenger's textual status to the corresponding value of the Status enum.
        /// </summary>
        /// <param name="status">Textual MSN status received from server</param>
        /// <returns>The corresponding enum value</returns>
        internal protected static PresenceStatus ParseStatus(string status)
        {
            switch (status)
            {
                case "NLN":
                    return PresenceStatus.Online;

                case "BSY":
                case "PHN":
                    return PresenceStatus.Busy;

                case "IDL":
                    return PresenceStatus.Idle;

                case "BRB":
                case "AWY":
                case "LUN":
                    return PresenceStatus.Away;

                case "FLN":
                    return PresenceStatus.Offline;

                case "HDN":
                    return PresenceStatus.Hidden;

                default:
                    break;
            }

            return PresenceStatus.Unknown;
        }


        /// <summary>
        /// Called when a UBX command has been received.
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnUBXReceived(NSMessage message)
        {
            //check the payload length
            if (message.CommandValues[1].ToString() == "0")
                return;

#if MSNP18
            string account = message.CommandValues[0].ToString().Split(':')[1];
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[0].ToString().Split(':')[0]);
#else
            string account = message.CommandValues[0].ToString();
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[1].ToString());
#endif

            if (message.InnerBody != null && account.ToLowerInvariant() == Owner.Mail.ToLowerInvariant() && type == ClientType.PassportMember)
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(new MemoryStream(message.InnerBody));
                XmlNodeList privateendpoints = xmlDoc.GetElementsByTagName("PrivateEndpointData");

                if (privateendpoints.Count > 0)
                {
                    Dictionary<Guid, string> newPlaces = new Dictionary<Guid, string>(privateendpoints.Count);
                    foreach (XmlNode pepdNode in privateendpoints)
                    {
                        string id = pepdNode.Attributes["id"].Value;
                        string epname = pepdNode["EpName"].InnerText;

                        newPlaces[new Guid(id)] = epname;

                        Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "Place: " + epname + " " + id, GetType().Name);
                   }

                   if (newPlaces.Count > 1 && Owner.MPOPMode == MPOP.AutoLogoff)
                   {
                       foreach (KeyValuePair<Guid, string> keyvalue in newPlaces)
                       {
                           if (keyvalue.Key != NSMessageHandler.MachineGuid)
                           {
                               owner.SignoutFrom(keyvalue.Key);
                           }
                       }
                   }

                    Owner.Places.Clear();
                    Owner.Places = newPlaces;
                }

                Owner.SetPersonalMessage(new PersonalMessage(message));
            }
            else
            {
                Contact contact = ContactList.GetContact(account, type);
                contact.SetPersonalMessage(new PersonalMessage(message));
            }
        }

        /// <summary>
        /// Called when a ILN command has been received.
        /// </summary>
        /// <remarks>
        /// ILN indicates the initial status of a contact. Used for MSNP15 and MSNP16, not MSNP18.
        /// It is send after initial log on or after adding/removing contact from the contactlist.
        /// Fires the <see cref="ContactOnline"/> and/or the <see cref="ContactStatusChanged"/> events.
        /// <code>ILN 0 [status] [account] [clienttype] [name] [clientcapacities:0] [displayimage] (MSNP16)</code>
        /// <code>ILN 0 [status] [account] [clienttype] [name] [clientcapacities] [displayimage] (MSNP15)</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnILNReceived(NSMessage message)
        {
            PresenceStatus newstatus = ParseStatus(message.CommandValues[1].ToString());
            string account = message.CommandValues[2].ToString().ToLowerInvariant();
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[3].ToString());
            string newname = (message.CommandValues.Count >= 5) ? message.CommandValues[4].ToString() : String.Empty;
            ClientCapacities newcaps = (ClientCapacities)Convert.ToInt64(((message.CommandValues.Count >= 6) ? (message.CommandValues[5].ToString().Contains(":") ? message.CommandValues[5].ToString().Split(':')[0] : message.CommandValues[5].ToString()) : "0"));
            string newdp = message.CommandValues.Count >= 7 ? message.CommandValues[6].ToString() : String.Empty;


            Contact contact = (account == Owner.Mail.ToLowerInvariant() && type == ClientType.PassportMember)
                ? Owner : ContactList.GetContact(account, type);

            contact.SetName(HttpUtility.UrlDecode(newname));

            if (newcaps != ClientCapacities.None)
            {
                contact.ClientCapacities = (ClientCapacities)newcaps;
            }

            if (contact != Owner && !String.IsNullOrEmpty(newdp))
            {
                DisplayImage userDisplay = new DisplayImage();
                userDisplay.Context = newdp;
                contact.DisplayImage = userDisplay;
            }

            PresenceStatus oldStatus = contact.Status;
            contact.SetStatus(newstatus);

            if (oldStatus == PresenceStatus.Unknown || oldStatus == PresenceStatus.Offline)
            {
                if (ContactOnline != null)
                    ContactOnline(this, new ContactEventArgs(contact));
            }

            if (ContactStatusChanged != null)
                ContactStatusChanged(this, new ContactStatusChangeEventArgs(contact, oldStatus));
        }

        /// <summary>
        /// Called when a NLN command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that a contact on the forward list went online.
        /// <code>NLN [status] [clienttype:account] [name] [clientcapacities:48] [displayimage] (MSNP18)</code>
        /// <code>NLN [status] [account] [clienttype] [name] [clientcapacities:0] [displayimage] (MSNP16)</code>
        /// <code>NLN [status] [account] [clienttype] [name] [clientcapacities] [displayimage] (MSNP15)</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnNLNReceived(NSMessage message)
        {
            PresenceStatus newstatus = ParseStatus(message.CommandValues[0].ToString());
#if MSNP18
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[1].ToString().Split(':')[0]);
            string account = message.CommandValues[1].ToString().Split(':')[1].ToLowerInvariant();
            string newname = (message.CommandValues.Count >= 3) ? message.CommandValues[2].ToString() : String.Empty;
            ClientCapacities newcaps = (ClientCapacities)Convert.ToInt64(((message.CommandValues.Count >= 4) ? (message.CommandValues[3].ToString().Contains(":") ? message.CommandValues[3].ToString().Split(':')[0] : message.CommandValues[3].ToString()) : "0"));
            string newdp = message.CommandValues.Count >= 5 ? message.CommandValues[4].ToString() : String.Empty;
#else
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[2].ToString());
            string account = message.CommandValues[1].ToString().ToLowerInvariant();
            string newname = (message.CommandValues.Count >= 4) ? message.CommandValues[3].ToString() : String.Empty;
            ClientCapacities newcaps = (ClientCapacities)Convert.ToInt64(((message.CommandValues.Count >= 5) ? (message.CommandValues[4].ToString().Contains(":") ? message.CommandValues[4].ToString().Split(':')[0] : message.CommandValues[4].ToString()) : "0"));
            string newdp = message.CommandValues.Count >= 6 ? message.CommandValues[5].ToString() : String.Empty;
#endif

            Contact contact = (account == Owner.Mail.ToLowerInvariant() && type == ClientType.PassportMember) ? Owner : ContactList.GetContact(account, type);
            contact.SetName(HttpUtility.UrlDecode(newname));
            if (newcaps != ClientCapacities.None)
            {
                contact.ClientCapacities = (ClientCapacities)newcaps;
            }

            if (contact != Owner && !String.IsNullOrEmpty(newdp))
            {
                DisplayImage userDisplay = new DisplayImage();
                userDisplay.Context = newdp;
                contact.DisplayImage = userDisplay;
            }

            PresenceStatus oldStatus = contact.Status;
            contact.SetStatus(newstatus);

            // The contact changed status
            if (ContactStatusChanged != null)
                ContactStatusChanged(this, new ContactStatusChangeEventArgs(contact, oldStatus));

            // The contact goes online
            if (ContactOnline != null)
                ContactOnline(this, new ContactEventArgs(contact));
        }

        /// <summary>
        /// Called when a FLN command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that a user went offline.
        /// <code>FLN [clienttype:account] [caps:0] [networkpng] (MSNP18)</code>
        /// <code>FLN [account] [clienttype] [caps:0] [networkpng] (MSNP16)</code>
        /// <code>FLN [account] [clienttype] [caps] [networkpng] (MSNP15)</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnFLNReceived(NSMessage message)
        {
#if MSNP18
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[0].ToString().Split(':')[0]);
            string account = message.CommandValues[0].ToString().Split(':')[1].ToLowerInvariant();
            ClientCapacities oldcaps = (ClientCapacities)Convert.ToInt64(((message.CommandValues.Count >= 2) ? (message.CommandValues[1].ToString().Contains(":") ? message.CommandValues[1].ToString().Split(':')[0] : message.CommandValues[1].ToString()) : "0"));
            string networkpng = (message.CommandValues.Count >= 3) ? message.CommandValues[2].ToString() : String.Empty;
#else
            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), message.CommandValues[1].ToString());
            string account = message.CommandValues[0].ToString().ToLowerInvariant();
            ClientCapacities newcaps = (ClientCapacities)Convert.ToInt64(((message.CommandValues.Count >= 3) ? (message.CommandValues[2].ToString().Contains(":") ? message.CommandValues[2].ToString().Split(':')[0] : message.CommandValues[2].ToString()) : "0"));
            string networkpng = (message.CommandValues.Count >= 4) ? message.CommandValues[3].ToString() : String.Empty;
#endif

            Contact contact = (account == Owner.Mail.ToLowerInvariant() && type == ClientType.PassportMember)
                ? Owner : ContactList.GetContact(account, type);

            PresenceStatus oldStatus = contact.Status;
            contact.SetStatus(PresenceStatus.Offline);

            // the contact changed status
            if (ContactStatusChanged != null)
                ContactStatusChanged(this, new ContactStatusChangeEventArgs(contact, oldStatus));

            // the contact goes offline
            if (ContactOffline != null)
                ContactOffline(this, new ContactEventArgs(contact));
        }

        /// <summary>
        /// Called when an OUT command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that the server has signed off the user.
        /// <code>OUT [Reason]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnOUTReceived(NSMessage message)
        {
            if (message.CommandValues.Count == 1)
            {
                switch (message.CommandValues[0].ToString().ToUpper(System.Globalization.CultureInfo.InvariantCulture))
                {
                    case "OTH":
                        OnSignedOff(new SignedOffEventArgs(SignedOffReason.OtherClient));
                        break;
                    case "SSD":
                        OnSignedOff(new SignedOffEventArgs(SignedOffReason.ServerDown));
                        break;
                    default:
                        OnSignedOff(new SignedOffEventArgs(SignedOffReason.None));
                        break;
                }
            }
            else
                OnSignedOff(new SignedOffEventArgs(SignedOffReason.None));
        }

        /// <summary>
        /// Called when a UBN command has been received.
        /// </summary>
        /// <remarks>
        /// <code>UBN [account;{GUID}] [1:xml data,2:sip invite, 3: MSNP2P SLP data, 4:logout, 10: unknown] [PayloadLegth]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnUBNReceived(NSMessage message)
        {
            NetworkMessage networkMessage = message as NetworkMessage;
            if (message.InnerBody != null)
            {
                switch (message.CommandValues[1].ToString())
                {
                    case "4":
                    case "8":
                        {
                            string logoutMsg = Encoding.UTF8.GetString(message.InnerBody);
                            if (logoutMsg.StartsWith("goawyplzthxbye") || logoutMsg == "gtfo")
                            {
                                // Logout here...
                                OnSignedOff(new SignedOffEventArgs(SignedOffReason.OtherClient));
                            }
                            return;
                        }
                }
            }
        }

        protected virtual void OnUUNReceived(NSMessage message)
        {
        }


        /// <summary>
        /// Called when a CHG command has been received.
        /// </summary>
        /// <remarks>
        /// The notification server has confirmed our request for a status change. 
        /// This function sets the status of the contactlist owner.
        /// <code>CHG [Transaction] [Status]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnCHGReceived(NSMessage message)
        {
            Owner.SetStatus(ParseStatus((string)message.CommandValues[1]));
#if MSNP16 || MSNP18
            Owner.EpName = Owner.EpName;
#endif
        }

        #endregion

        #region Switchboard

        /// <summary>
        /// Class used for items stored in the switchboard queue.
        /// </summary>
        protected class SwitchboardQueueItem
        {
            /// <summary>
            /// The object that initiated the request.
            /// </summary>
            public object Initiator;
            /// <summary>
            /// The switchboard handler that will be handling the new switchboard session.
            /// </summary>
            public SBMessageHandler SwitchboardHandler;

            /// <summary>
            /// Constructs a queue item.
            /// </summary>
            /// <param name="switchboardHandler"></param>
            /// <param name="initiator"></param>
            public SwitchboardQueueItem(SBMessageHandler switchboardHandler, object initiator)
            {
                Initiator = initiator;
                SwitchboardHandler = switchboardHandler;
            }
        }

        /// <summary>
        /// Gets a new switchboard handler object. Called when a remote client initiated the switchboard.
        /// </summary>
        /// <param name="processor"></param>
        /// <param name="sessionHash"></param>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        protected virtual IMessageHandler CreateSBHandler(IMessageProcessor processor, string sessionHash, int sessionId)
        {
            SBMessageHandler handler = Factory.CreateSwitchboardHandler();
            handler.MessageProcessor = processor;
            handler.NSMessageHandler = this;
            handler.SetInvitation(sessionHash, sessionId);

            return handler;
        }

        /// <summary>
        /// Fires the <see cref="SBCreated"/> event.
        /// </summary>
        /// <param name="switchboard">The switchboard created</param>
        /// <param name="initiator">The object that initiated the switchboard request.</param>
        /// <param name="account"></param>
        /// <param name="name"></param>
        /// <param name="anonymous">Indecates that whether it is an anonymous request.</param>
        protected virtual void OnSBCreated(SBMessageHandler switchboard, object initiator, string account, string name, bool anonymous)
        {
            if (SBCreated != null)
                SBCreated(this, new SBCreatedEventArgs(switchboard, initiator, account, name, anonymous));
        }

        /// <summary>
        /// Called when a RNG command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that the user receives a switchboard session (chatsession) request. A connection to the switchboard will be established
        /// and the corresponding events and objects are created.
        /// <code>RNG [Session] [IP:Port] 'CKI' [Hash] [Account] [Name] U messenger.msn.com 1</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnRNGReceived(NSMessage message)
        {
            SBMessageProcessor processor = Factory.CreateSwitchboardProcessor();

            // set new connectivity settings
            ConnectivitySettings newSettings = new ConnectivitySettings(processor.ConnectivitySettings);
            string[] values = ((string)message.CommandValues[1]).Split(new char[] { ':' });

            newSettings.Host = values[0];
            newSettings.Port = int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);
            processor.ConnectivitySettings = newSettings;

            // create a switchboard object
            SBMessageHandler handler = (SBMessageHandler)CreateSBHandler(processor, message.CommandValues[3].ToString(), int.Parse((string)message.CommandValues[0], System.Globalization.CultureInfo.InvariantCulture));

            processor.RegisterHandler(handler);

            // start connecting
            processor.Connect();

            // notify the client
            string account = string.Empty;
            string name = string.Empty;
            bool anonymous = false;

            if (message.CommandValues.Count >= 5)
            {
                account = message.CommandValues[4].ToString();
            }

            if (message.CommandValues.Count >= 6)
            {
                name = message.CommandValues[5].ToString();
            }

            if (ContactList.GetContact(account) == null)
            {
                anonymous = true;
            }

            OnSBCreated(handler, null, account, name, anonymous);
        }

        /// <summary>
        /// Called when a XFR command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that the notification server has send us the location of a switch-board server in order to
        /// make contact with a client, or that we must switch to a new notification server.
        /// <code>XFR [Transaction] [SB|NS] [IP:Port] ['0'|'CKI'] [Hash|CurrentIP:CurrentPort]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnXFRReceived(NSMessage message)
        {
            if ((string)message.CommandValues[1] == "NS")
            {
                // switch to a new notification server. That means reconnecting our current message processor.
                SocketMessageProcessor processor = (SocketMessageProcessor)MessageProcessor;

                // disconnect first
                processor.Disconnect();

                // set new connectivity settings
                ConnectivitySettings newSettings = new ConnectivitySettings(processor.ConnectivitySettings);
                string[] values = ((string)message.CommandValues[2]).Split(new char[] { ':' });

                newSettings.Host = values[0];
                newSettings.Port = int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);

                processor.ConnectivitySettings = newSettings;

                // and reconnect. The login procedure will now start over again
                processor.Connect();
            }
            if ((string)message.CommandValues[1] == "SB")
            {
                if (pendingSwitchboards.Count > 0)
                {
                    if (Owner.Status == PresenceStatus.Offline)
                        System.Diagnostics.Trace.WriteLine("Owner not yet online!", "NS15MessageHandler");

                    SBMessageProcessor processor = Factory.CreateSwitchboardProcessor();
                    SwitchboardQueueItem queueItem = (SwitchboardQueueItem)pendingSwitchboards.Dequeue();

                    // set new connectivity settings
                    ConnectivitySettings newSettings = new ConnectivitySettings(processor.ConnectivitySettings);
                    string[] values = ((string)message.CommandValues[2]).Split(new char[] { ':' });

                    newSettings.Host = values[0];
                    newSettings.Port = int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);

                    Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "Switchboard connectivity settings: " + newSettings.ToString(), GetType().Name);

                    processor.ConnectivitySettings = newSettings;

                    // set the switchboard objects with the processor values
                    // SBMessageHandler handler = (SBMessageHandler)CreateSBHandler(processor, message.CommandValues[4].ToString());
                    string sessionHash = message.CommandValues[4].ToString();
                    queueItem.SwitchboardHandler.SetInvitation(sessionHash);
                    queueItem.SwitchboardHandler.MessageProcessor = processor;
                    queueItem.SwitchboardHandler.NSMessageHandler = this;

                    // register this handler so the switchboard can respond
                    processor.RegisterHandler(queueItem.SwitchboardHandler);

                    // notify the client
                    OnSBCreated(queueItem.SwitchboardHandler, queueItem.Initiator, string.Empty, string.Empty, true);

                    Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "SB created event handler called", GetType().Name);

                    // start connecting
                    processor.Connect();

                    Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "Opening switchboard connection...", GetType().Name);
                }
                else
                {
                    Trace.WriteLineIf(Settings.TraceSwitch.TraceWarning, "Switchboard request received, but no pending switchboards available.", GetType().Name);
                }
            }
        }

        /// <summary>
        /// Called when a UBM command has been received, this message was sent by a Yahoo Messenger client.
        /// </summary>
        /// <remarks>
        /// Indicates that the notification server has send us a UBM. This is usually a message from Yahoo Messenger.
        /// <code>UBM [Remote user account] 32 [Destination user account] [3(nudge) or 2(typing) or 1(text message)] [Length]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnUBMReceived(NSMessage message)
        {
            string sender = message.CommandValues[0].ToString();
            YIMMessage msg = new YIMMessage(message);

            if ((!msg.InnerMessage.MimeHeader.ContainsKey("TypingUser"))
                && ContactList.HasContact(sender, ClientType.EmailMember))
            {
                lock (SwitchBoards)
                {
                    foreach (YIMMessageHandler YimHandler in SwitchBoards)
                    {
                        if (YimHandler.Contacts.ContainsKey(sender))
                        {
                            return;  //The handler have been registered, return.
                        }
                    }
                }

                YIMMessageHandler switchboard = Factory.CreateYIMMessageHandler();
                switchboard.NSMessageHandler = this;
                switchboard.Contacts[sender] = ContactConversationState.Invited;

                switchboard.MessageProcessor = MessageProcessor;
                lock (SwitchBoards)
                    SwitchBoards.Add(switchboard);

                OnSBCreated(switchboard, null, sender, sender, false);
                switchboard.ForceJoin(ContactList[sender, ClientType.EmailMember]);
                MessageProcessor.RegisterHandler(switchboard);
                switchboard.HandleMessage(MessageProcessor, msg);
            }
        }

        #endregion

        #region Mail and Notification

        /// <summary>
        /// Called when a NOT command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that a notification message has been received.
        /// <code>NOT [body length]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnNOTReceived(NSMessage message)
        {
            // build a notification message
            NotificationMessage notification = new NotificationMessage(message);

            Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "Notification received : " + notification.ToDebugString(), GetType().Name);
        }

        /// <summary>
        /// Called when a MSG command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that the notification server has send us a MSG.
        /// This is usually a MSG from the 'HOTMAIL' user (account is also 'HOTMAIL') which includes notifications
        /// about the contact list owner's profile, new mail, number of unread mails in the inbox, offline messages, etc.
        /// <code>MSG [Account] [Name] [Length]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnMSGReceived(MSNMessage message)
        {
            MSGMessage msgMessage = new MSGMessage(message);
            string mime = msgMessage.MimeHeader["Content-Type"].ToString();

            if (mime.IndexOf("text/x-msmsgsprofile") >= 0)
            {
                StrDictionary hdr = msgMessage.MimeHeader;

                int clientPort = 0;

                if (hdr.ContainsKey("ClientPort"))
                {
                    clientPort = int.Parse(msgMessage.MimeHeader["ClientPort"].Replace('.', ' '), System.Globalization.CultureInfo.InvariantCulture);
                    clientPort = ((clientPort & 255) * 256) + ((clientPort & 65280) / 256);
                }

                IPAddress ip = hdr.ContainsKey("ClientIP") ? IPAddress.Parse(hdr["ClientIP"]) : IPAddress.None;
                Owner.UpdateProfile(
                    hdr["LoginTime"],
                    (hdr.ContainsKey("EmailEnabled")) ? (Convert.ToInt32(hdr["EmailEnabled"]) == 1) : false,
                    hdr["MemberIdHigh"],
                    hdr["MemberIdLow"],
                    hdr["lang_preference"],
                    hdr["preferredEmail"],
                    hdr["country"],
                    hdr["PostalCode"],
                    hdr["Gender"],
                    hdr["Kid"],
                    hdr["Age"],
                    hdr["Birthday"],
                    hdr["Wallet"],
                    hdr["sid"],
                    hdr["kv"],
                    hdr["MSPAuth"],
                    ip,
                    clientPort,
                    hdr.ContainsKey("Nickname") ? hdr["Nickname"] : String.Empty,
                    hdr.ContainsKey("MPOPEnabled") && hdr["MPOPEnabled"] != "0",
                    hdr.ContainsKey("RouteInfo") ? hdr["RouteInfo"] : String.Empty
                );

                if (IPAddress.None != ip)
                {
                    // set the external end point. This can be used in file transfer connectivity determing
                    externalEndPoint = new IPEndPoint(ip, clientPort);
                }

                if (AutoSynchronize)
                {
                    ContactService.SynchronizeContactList();
                }
                else
                {
                    OnSignedIn(EventArgs.Empty);
                }
            }
            else if (mime.IndexOf("x-msmsgsemailnotification") >= 0)
            {
                message.InnerBody = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(message.InnerBody).Replace("\r\n\r\n", "\r\n"));
                msgMessage = new MSGMessage(message);

                OnMailNotificationReceived(new NewMailEventArgs(
                    (string)msgMessage.MimeHeader["From"],
                    (string)msgMessage.MimeHeader["Message-URL"],
                    (string)msgMessage.MimeHeader["Post-URL"],
                    (string)msgMessage.MimeHeader["Subject"],
                    (string)msgMessage.MimeHeader["Dest-Folder"],
                    (string)msgMessage.MimeHeader["From-Addr"],
                    int.Parse((string)msgMessage.MimeHeader["id"], System.Globalization.CultureInfo.InvariantCulture)
                ));
            }
            else if (mime.IndexOf("x-msmsgsactivemailnotification") >= 0)
            {
                //Now this is the unread OIM info, not the new mail.
                message.InnerBody = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(message.InnerBody).Replace("\r\n\r\n", "\r\n"));
                msgMessage = new MSGMessage(message);

                OnMailChanged(new MailChangedEventArgs(
                    (string)msgMessage.MimeHeader["Src-Folder"],
                    (string)msgMessage.MimeHeader["Dest-Folder"],
                    int.Parse((string)msgMessage.MimeHeader["Message-Delta"], System.Globalization.CultureInfo.InvariantCulture)
                ));
            }
            else if (mime.IndexOf("x-msmsgsinitialemailnotification") >= 0)
            {
                OnMailboxStatusReceived(new MailboxStatusEventArgs(
                    int.Parse((string)msgMessage.MimeHeader["Inbox-Unread"], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse((string)msgMessage.MimeHeader["Folders-Unread"], System.Globalization.CultureInfo.InvariantCulture),
                    (string)msgMessage.MimeHeader["Inbox-URL"],
                    (string)msgMessage.MimeHeader["Folders-URL"],
                    (string)msgMessage.MimeHeader["Post-URL"]
                ));
            }
            else if (mime.IndexOf("x-msmsgsinitialmdatanotification") >= 0 || mime.IndexOf("x-msmsgsoimnotification") >= 0)
            {
                message.InnerBody = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(message.InnerBody).Replace("\r\n\r\n", "\r\n"));
                msgMessage = new MSGMessage(message);


                /*
                 * <MD>
                 *     <E>
                 *         <I>884</I>     Inbox total
                 *         <IU>0</IU>     Inbox unread mail
                 *         <O>222</O>     Sent + Junk + Drafts
                 *         <OU>15</OU>    Junk unread mail
                 *     </E>
                 *     <Q>
                 *         <QTM>409600</QTM>
                 *         <QNM>204800</QNM>
                 *     </Q>
                 *     <M>
                 *         <!-- OIM Nodes -->
                 *     </M>
                 *     <M>
                 *         <!-- OIM Nodes -->
                 *     </M>
                 * </MD>
                 */

                string xmlstr = msgMessage.MimeHeader["Mail-Data"];
                try
                {
                    XmlDocument xdoc = new XmlDocument();
                    xdoc.LoadXml(xmlstr);
                    XmlNodeList mdnodelist = xdoc.GetElementsByTagName("MD");
                    if (mdnodelist.Count > 0)
                    {
                        foreach (XmlNode node in mdnodelist[0])
                        {
                            if (node.Name.ToLowerInvariant() == "e" && node.HasChildNodes)
                            {
                                int iu = 0;
                                int ou = 0;
                                foreach (XmlNode cnode in node.ChildNodes)
                                {
                                    if (cnode.Name.ToLowerInvariant() == "iu")
                                    {
                                        int.TryParse(cnode.InnerText, out iu);
                                        break;
                                    }
                                }

                                foreach (XmlNode cnode in node.ChildNodes)
                                {
                                    if (cnode.Name.ToLowerInvariant() == "ou")
                                    {
                                        int.TryParse(cnode.InnerText, out ou);
                                        break;
                                    }
                                }

                                OnMailboxStatusReceived(new MailboxStatusEventArgs(
                                        iu,
                                        ou,
                                        (string)msgMessage.MimeHeader["Inbox-URL"],      //Invalid, I think it should be obsolated.
                                        (string)msgMessage.MimeHeader["Folders-URL"],    //Invalid, I think it should be obsolated.
                                        (string)msgMessage.MimeHeader["Post-URL"]
                                    ));
                                break;
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    Trace.WriteLineIf(Settings.TraceSwitch.TraceError, ex.Message, GetType().Name);
                }

                OIMService.ProcessOIM(msgMessage, mime.IndexOf("x-msmsgsinitialmdatanotification") >= 0);
            }
        }

        /// <summary>
        /// Fires the <see cref="MailboxChanged"/> event.
        /// </summary>
        /// <remarks>Called when the owner has removed or moved e-mail.</remarks>
        /// <param name="e"></param>
        protected virtual void OnMailChanged(MailChangedEventArgs e)
        {
            if (MailboxChanged != null)
            {
                MailboxChanged(this, e);
            }
        }

        /// <summary>
        /// Fires the <see cref="MailboxStatusReceived"/> event.
        /// </summary>
        /// <remarks>Called when the server sends the status of the owner's mailbox.</remarks>
        /// <param name="e"></param>
        protected virtual void OnMailboxStatusReceived(MailboxStatusEventArgs e)
        {
            if (MailboxStatusReceived != null)
            {
                MailboxStatusReceived(this, e);
            }
        }

        /// <summary>
        /// Fires the <see cref="NewMailReceived"/> event.
        /// </summary>
        /// <remarks>Called when the owner has received new e-mail, or e-mail has been removed / moved.</remarks>
        /// <param name="e"></param>
        protected virtual void OnMailNotificationReceived(NewMailEventArgs e)
        {
            if (NewMailReceived != null)
            {
                NewMailReceived(this, e);
            }
        }

        #endregion

        #region Contact and Group

        /// <summary>
        /// Translates the codes used by the MSN server to a MSNList object.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        protected virtual MSNLists GetMSNList(string name)
        {
            switch (name)
            {
                case "AL":
                    return MSNLists.AllowedList;
                case "FL":
                    return MSNLists.ForwardList;
                case "BL":
                    return MSNLists.BlockedList;
                case "RL":
                    return MSNLists.ReverseList;
                case "PL":
                    return MSNLists.PendingList;
            }
            throw new MSNPSharpException("Unknown MSNList type");
        }

        /// <summary>
        /// Called when a ADL command has been received.
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnADLReceived(NSMessage message)
        {
            if (message.TransactionID != 0 &&
                message.CommandValues[1].ToString() == "OK" &&
                ContactService.ProcessADL(message.TransactionID))
            {
                // All initial ADLs have processed.
                if (0 == ContactService.initialADLcount)
                {
                    Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "All initial ADLs have processed.", GetType().Name);
                }
            }
            else
            {
                NetworkMessage networkMessage = message as NetworkMessage;
                if (networkMessage.InnerBody != null) //Payload ADL command
                {
                    if (AutoSynchronize)
                    {
                        ContactService.msRequest(
                            "MessengerPendingList",
                            delegate
                            {
                                XmlDocument xmlDoc = new XmlDocument();
                                xmlDoc.Load(new MemoryStream(networkMessage.InnerBody));
                                XmlNodeList domains = xmlDoc.GetElementsByTagName("d");
                                string domain = String.Empty;
                                foreach (XmlNode domainNode in domains)
                                {
                                    domain = domainNode.Attributes["n"].Value;
                                    XmlNode contactNode = domainNode.FirstChild;
                                    do
                                    {
                                        string account = contactNode.Attributes["n"].Value + "@" + domain;
                                        ClientType type = (ClientType)int.Parse(contactNode.Attributes["t"].Value);
                                        MSNLists list = (MSNLists)int.Parse(contactNode.Attributes["l"].Value);
                                        string displayName = account;
                                        try
                                        {
                                            displayName = contactNode.Attributes["f"].Value;
                                        }
                                        catch (Exception)
                                        {
                                        }

                                        if (ContactList.HasContact(account, type))
                                        {
                                            Contact contact = ContactList.GetContact(account, type);

                                            // Fire ReverseAdded. If this contact on Pending list other person added us, otherwise we added and other person accepted.
                                            if (contact.OnPendingList || (contact.OnReverseList && !contact.OnAllowedList && !contact.OnBlockedList))
                                            {
                                                ContactService.OnReverseAdded(new ContactEventArgs(contact));
                                            }
                                        }

                                        Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, account + ":" + type + " was added to your " + list.ToString(), GetType().Name);

                                    } while (contactNode.NextSibling != null);
                                }

                            });
                    }
                }
            }
        }

        /// <summary>
        /// Called when a RML command has been received.
        /// </summary>
        /// <remarks>Indicates that someone was removed from a list by local user (RML [Trans-ID] OK)
        /// or local user was removed from someone's reverse list (RML 0 [Trans-ID]\r\n[payload]).</remarks>
        /// <param name="nsMessage"></param>
        protected virtual void OnRMLReceived(NSMessage nsMessage)
        {
            NetworkMessage networkMessage = nsMessage as NetworkMessage;
            if (networkMessage.InnerBody != null)   //Payload RML command.
            {
                if (AutoSynchronize)
                {
                    ContactService.msRequest("Initial", null);
                }

                if (Settings.TraceSwitch.TraceVerbose)
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(new MemoryStream(networkMessage.InnerBody));
                    XmlNodeList domains = xmlDoc.GetElementsByTagName("d");
                    string domain = String.Empty;
                    foreach (XmlNode domainNode in domains)
                    {
                        domain = domainNode.Attributes["n"].Value;
                        XmlNode contactNode = domainNode.FirstChild;
                        do
                        {
                            string account = contactNode.Attributes["n"].Value + "@" + domain;
                            ClientType type = (ClientType)int.Parse(contactNode.Attributes["t"].Value);
                            MSNLists list = (MSNLists)int.Parse(contactNode.Attributes["l"].Value);
                            account = account.ToLower(CultureInfo.InvariantCulture);

                            Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, account + " has removed you from his/her " + list.ToString(), GetType().Name);

                        } while (contactNode.NextSibling != null);
                    }
                }
            }
        }

        /// <summary>
        /// Called when an ADG command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that a contact group has been added to the contact group list.
        /// Raises the <see cref="ContactGroupAdded"/> event.
        /// <code>ADG [Transaction] [ListVersion] [Name] [GroupID] </code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnADGReceived(NSMessage message)
        {
            if (AutoSynchronize)
            {
                ContactService.abRequest("ContactSave", null);
            }
        }

        /// <summary>
        /// Called when a RMG command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that a contact group has been removed.
        /// Raises the <see cref="ContactGroupRemoved"/> event.
        /// <code>RMG [Transaction] [ListVersion] [GroupID]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnRMGReceived(NSMessage message)
        {
            if (AutoSynchronize)
            {
                ContactService.abRequest("ContactSave", null);
            }
        }

        /// <summary>Called when a FQY command has been received.
        /// <remarks>Indicates a client has different network types except PassportMember.</remarks>
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnFQYReceived(NSMessage message)
        {
            // <ml><d n="domain"><c n="username" [t="clienttype"] /></d></ml>
            NetworkMessage networkMessage = message as NetworkMessage;
            if (networkMessage.InnerBody != null)
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(new MemoryStream(networkMessage.InnerBody));
                XmlNodeList domains = xmlDoc.GetElementsByTagName("d");
                string domain = String.Empty;
                foreach (XmlNode domainNode in domains)
                {
                    domain = domainNode.Attributes["n"].Value;
                    XmlNode contactNode = domainNode.FirstChild;
                    do
                    {
                        if (contactNode.Attributes["t"] != null)
                        {
                            ClientType type = (ClientType)Enum.Parse(typeof(ClientType), contactNode.Attributes["t"].Value);
                            string account = contactNode.Attributes["n"].Value + "@" + domain;
                            account = account.ToLower(CultureInfo.InvariantCulture);

                            if (!ContactList.HasContact(account, type))
                            {
                                ContactService.AddNewContact(account, type, String.Empty);
                            }
                        }

                    } while (contactNode.NextSibling != null);
                }
            }
        }


        #endregion

        #region Challenge and Ping

        /// <summary>
        /// Called when a CHL (challenge) command message has been received.
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnCHLReceived(NSMessage message)
        {
            if (Credentials == null)
                throw new MSNPSharpException("No credentials available for the NSMSNP15 handler. No challenge answer could be send.");

            string payload = QRYFactory.CreateQRY(Credentials.ClientID, Credentials.ClientCode, message.CommandValues[1].ToString());
            MSNTicket.OIMLockKey = payload;
            MessageProcessor.SendMessage(new NSPayLoadMessage("QRY", new string[] { Credentials.ClientID }, payload));
        }

        /// <summary>
        /// Called when an PRP command has been received.
        /// </summary>
        /// <remarks>
        /// Informs about the phone numbers of the contact list owner.
        /// <code>PRP [TransactionID] [ListVersion] PhoneType Number</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnPRPReceived(NSMessage message)
        {
            string number = String.Empty;
            string type = String.Empty;
            if (message.CommandValues.Count >= 4)
            {
                if (message.CommandValues.Count >= 4)
                    number = HttpUtility.UrlDecode((string)message.CommandValues[3]);
                else
                    number = String.Empty;
                type = message.CommandValues[1].ToString();
            }
            else
            {
                number = HttpUtility.UrlDecode((string)message.CommandValues[2]);
                type = message.CommandValues[1].ToString();
            }

            switch (type)
            {
                case "PHH":
                    Owner.SetHomePhone(number);
                    break;
                case "PHW":
                    Owner.SetWorkPhone(number);
                    break;
                case "PHM":
                    Owner.SetMobilePhone(number);
                    break;
                case "MBE":
                    Owner.SetMobileDevice((number == "Y") ? true : false);
                    break;
                case "MOB":
                    Owner.SetMobileAccess((number == "Y") ? true : false);
                    break;
                case "MFN":
                    Owner.SetName(HttpUtility.UrlDecode((string)message.CommandValues[2]));
                    break;
            }
        }


        /// <summary>
        /// Called when a QRY (challenge) command message has been received.
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnQRYReceived(NSMessage message)
        {

        }

        /// <summary>
        /// Called when a QNG command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates a ping answer. The number of seconds indicates the timespan in which another ping must be send.
        /// <code>QNG [Seconds]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnQNGReceived(NSMessage message)
        {
            if (PingAnswer != null)
            {
                // get the number of seconds till the next ping and fire the event
                // with the correct parameters.
                int seconds = int.Parse((string)message.CommandValues[0], System.Globalization.CultureInfo.InvariantCulture);
                PingAnswer(this, new PingAnswerEventArgs(seconds));
            }
        }



        #endregion

        #region Privacy

        /// <summary>
        /// Called when a BLP command has been received.
        /// </summary>
        /// <remarks>
        /// Indicates that the server has send the privacy mode for the contact list owner.
        /// <code>BLP [Transaction] [SynchronizationID] [PrivacyMode]</code>
        /// </remarks>
        /// <param name="message"></param>
        protected virtual void OnBLPReceived(NSMessage message)
        {
            if (Owner == null)
                return;

            string type = String.Empty;

            if (message.CommandValues.Count == 1)
                type = message.CommandValues[0].ToString();
            else
                type = message.CommandValues[1].ToString();

            switch (type)
            {
                case "AL":
                    Owner.SetPrivacy(PrivacyMode.AllExceptBlocked);
                    if (AutoSynchronize)
                    {
                        ContactService.UpdateMe();
                    }
                    break;
                case "BL":
                    owner.SetPrivacy(PrivacyMode.NoneButAllowed);
                    if (AutoSynchronize)
                    {
                        ContactService.UpdateMe();
                    }
                    break;
            }
        }

        /// <summary>
        /// Called when a GCF command has been received. 
        /// </summary>
        /// <remarks>Indicates that the server has send bad words for messaging.</remarks>
        /// <param name="message"></param>
        protected virtual void OnGCFReceived(NSMessage message)
        {
            NetworkMessage networkMessage = message as NetworkMessage;
            if (networkMessage.InnerBody != null)
            {
                censorWords.Clear();
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(new MemoryStream(networkMessage.InnerBody));
                XmlNodeList imtexts = xmlDoc.GetElementsByTagName("imtext");
                foreach (XmlNode imtextNode in imtexts)
                {
                    string censor = Encoding.UTF8.GetString(Convert.FromBase64String(imtextNode.Attributes["value"].Value));
                    censorWords.Add(new Regex(censor));
                    Trace.WriteLineIf(Settings.TraceSwitch.TraceVerbose, "Censor: " + censor, GetType().Name);
                }
            }
        }

        #endregion

        #endregion

        #region Command handler

        private EventHandler<EventArgs> processorConnectedHandler;
        private EventHandler<EventArgs> processorDisconnectedHandler;

        /// <summary>
        /// Event handler.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NSMessageHandler_ProcessorConnectCallback(object sender, EventArgs e)
        {
            Clear();
            OnProcessorConnectCallback((IMessageProcessor)sender);
        }

        /// <summary>
        /// Event handler.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NSMessageHandler_ProcessorDisconnectCallback(object sender, EventArgs e)
        {
            if (IsSignedIn)
                OnSignedOff(new SignedOffEventArgs(SignedOffReason.None));

            OnProcessorDisconnectCallback((IMessageProcessor)sender);

            Clear();
        }

        /// <summary>
        /// Clears all resources associated with a nameserver session.
        /// </summary>
        /// <remarks>
        /// Called after we the processor has disconnected. This will clear the contactlist and free other resources.
        /// </remarks>
        protected virtual void Clear()
        {
            ContactList.Clear();
            ContactGroups.Clear();
            ContactService.Clear();
            SwitchBoards.Clear();
            Owner.Emoticons.Clear();
            Owner.Places.Clear();
            externalEndPoint = null;
            isSignedIn = false;
            msnticket = MSNTicket.Empty;
        }

        /// <summary>
        /// Handles message from the processor.
        /// </summary>
        /// <remarks>
        /// This is one of the most important functions of the class.
        /// It handles incoming messages and performs actions based on the commands in the messages.
        /// Many commands will affect the data objects in MSNPSharp, like <see cref="Contact"/> and <see cref="ContactGroup"/>.
        /// For example contacts are renamed, contactgroups are added and status is set.
        /// Exceptions which occur in this method are redirected via the <see cref="ExceptionOccurred"/> event.
        /// </remarks>
        /// <param name="sender">The message processor that dispatched the message.</param>
        /// <param name="message">The network message received from the notification server</param>
        public virtual void HandleMessage(IMessageProcessor sender, NetworkMessage message)
        {
            try
            {
                // We expect at least a NSMessage object
                NSMessage nsMessage = (NSMessage)message;
                switch (nsMessage.Command)
                {
                    case "ADG":
                        OnADGReceived(nsMessage);
                        return;
                    case "ADL":
                        OnADLReceived(nsMessage);
                        return;
                    case "BLP":
                        OnBLPReceived(nsMessage);
                        return;
                    case "BPR":
                        OnBPRReceived(nsMessage);
                        return;
                    case "CHG":
                        OnCHGReceived(nsMessage);
                        return;
                    case "CHL":
                        OnCHLReceived(nsMessage);
                        return;
                    case "CVR":
                        OnCVRReceived(nsMessage);
                        return;
                    case "FLN":
                        OnFLNReceived(nsMessage);
                        return;
                    case "FQY":
                        OnFQYReceived(nsMessage);
                        return;
                    case "GCF":
                        OnGCFReceived(nsMessage);
                        return;
                    case "ILN":
                        OnILNReceived(nsMessage);
                        return;
                    case "MSG":
                        OnMSGReceived(nsMessage);
                        return;
                    case "NLN":
                        OnNLNReceived(nsMessage);
                        return;
                    case "NOT":
                        OnNOTReceived(nsMessage);
                        return;
                    case "OUT":
                        OnOUTReceived(nsMessage);
                        return;
                    case "PRP":
                        OnPRPReceived(nsMessage);
                        return;
                    case "QNG":
                        OnQNGReceived(nsMessage);
                        return;
                    case "QRY":
                        OnQRYReceived(nsMessage);
                        return;
                    case "RMG":
                        OnRMGReceived(nsMessage);
                        return;
                    case "RML":
                        OnRMLReceived(nsMessage);
                        return;
                    case "RNG":
                        OnRNGReceived(nsMessage);
                        return;
                    case "UBM":
                        OnUBMReceived(nsMessage);
                        return;
                    case "UBN":
                        OnUBNReceived(nsMessage);
                        return;
                    case "UUN":
                        OnUUNReceived(nsMessage);
                        return;
                    case "UBX":
                        OnUBXReceived(nsMessage);
                        return;
                    case "USR":
                        OnUSRReceived(nsMessage);
                        return;
                    case "VER":
                        OnVERReceived(nsMessage);
                        return;
                    case "XFR":
                        OnXFRReceived(nsMessage);
                        return;
                }

                // Check whether it is a numeric error command
                if (nsMessage.Command[0] >= '0' && nsMessage.Command[0] <= '9')
                {
                    MSNError msnError = 0;
                    try
                    {
                        int errorCode = int.Parse(nsMessage.Command, System.Globalization.CultureInfo.InvariantCulture);
                        msnError = (MSNError)errorCode;
                    }
                    catch (Exception fe)
                    {
                        throw new MSNPSharpException("Exception Occurred when parsing an error code received from the server", fe);
                    }
                    OnServerErrorReceived(new MSNErrorEventArgs(msnError));
                }
                else
                {
                    // It is a unknown command
                    Trace.WriteLineIf(Settings.TraceSwitch.TraceWarning, "UNKNOWN COMMAND: " + nsMessage.Command + "\r\n" + nsMessage.ToDebugString(), GetType().ToString());
                }
            }
            catch (Exception e)
            {
                OnExceptionOccurred(new ExceptionEventArgs(e));
                throw e;
            }
        }

        /// <summary>
        /// Fires the <see cref="ServerErrorReceived"/> event.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnServerErrorReceived(MSNErrorEventArgs e)
        {
            if (ServerErrorReceived != null)
                ServerErrorReceived(this, e);
        }

        /// <summary>
        /// Fires the <see cref="ExceptionOccurred"/> event.
        /// </summary>
        /// <param name="e">The exception event args</param>
        protected virtual void OnExceptionOccurred(ExceptionEventArgs e)
        {
            if (ExceptionOccurred != null)
                ExceptionOccurred(this, e);

            Trace.WriteLineIf(Settings.TraceSwitch.TraceError, e.ToString(), GetType().Name);
        }

        /// <summary>
        /// Fires the <see cref="AuthenticationError"/> event.
        /// </summary>
        /// <param name="e">The exception event args</param>
        protected virtual void OnAuthenticationErrorOccurred(ExceptionEventArgs e)
        {
            if (AuthenticationError != null)
                AuthenticationError(this, e);

            Trace.WriteLineIf(Settings.TraceSwitch.TraceError, e.ToString(), GetType().Name);
        }

        #endregion
    }
};