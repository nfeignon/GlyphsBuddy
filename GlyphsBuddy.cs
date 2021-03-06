﻿// Credits to Apoc, Main and Aash

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;
using CommonBehaviors.Actions;
using Styx;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Routines;
using Styx.CommonBot.Coroutines;
using Styx.Helpers;
using Styx.Pathing;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Action = Styx.TreeSharp.Action;

namespace Toto
{

    internal class GlyphsBuddy : BotBase
    {
        private LocalPlayer Me { get { return StyxWoW.Me; } }
        private readonly Random Rand = new Random();
        private const NumberStyles Style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        private readonly CultureInfo Culture = CultureInfo.CreateSpecificCulture("en-US");
        private readonly Version _version = new Version(0, 1);
        public GlyphsBuddy Instance { get; private set; }
		
		private MoveResult _lastMoveResult;
		private WoWPoint _gotoLocation;
		
		private bool milling;

		// default
		private WoWPoint AH_WAYPOINT;
		private WoWPoint AUCTIONEER_LOCATION;
		private WoWPoint MAILBOX_LOCATION;
		private WoWPoint TRADER_LOCATION;
		private int _auctioneerId;
		private int _inkTraderId;
		private int _mailboxId;
		
        public GlyphsBuddy()
        {
            Instance = this;
			milling = false;
        }

        public override string Name
        {
            get { return "GlyphsBuddy"; }
        }

        private Composite _root;
		public override Composite Root
        {
            get
			{
				return _root ?? (_root = new PrioritySelector(new ActionRunCoroutine(ctx => RootLogic())));
			}
        }
		
		private void initWaypoints() {
			if (Me.MapId == 571) { // dalaran, behind the ink trader
				AH_WAYPOINT = new WoWPoint(5763.545, 744.684, 653.6647);
				AUCTIONEER_LOCATION = new WoWPoint(5927.629, 731.5903, 643.17);
				MAILBOX_LOCATION = new WoWPoint(5887.521, 717.3115, 640.6613);
				TRADER_LOCATION = new WoWPoint(5865.998, 707.1326, 643.272);
				_auctioneerId = 35594;
				_inkTraderId = 33027;
				_mailboxId = 191955;
			} else { // default SW
				AH_WAYPOINT = new WoWPoint(-8848.92, 642.36, 96.50);
				AUCTIONEER_LOCATION = new WoWPoint(-8816.13, 659.97, 98.11);
				MAILBOX_LOCATION = new WoWPoint(-8862.36, 638.29, 96.34);
				TRADER_LOCATION = new WoWPoint(-8862.07, 859.77, 99.61);
				_auctioneerId = 8719;
				_inkTraderId = 30730;       	// Stanly McCormick, inscription supplies stormwind
				_mailboxId = 197135;		// mailbox in front of AH
			}
		}
		
		public async Task<bool> RootLogic()
        {
			Log("Init Root Logic");
			initWaypoints();
            Log("Start of Root Logic");
			milling = true;

			Log("Go to auctioneer");
			//await MoveTo(AH_WAYPOINT);
			await MoveTo(AUCTIONEER_LOCATION);
			
			await CancelUndercutAuctions();
			
			Log("Go to mailbox");
			await MoveTo(MAILBOX_LOCATION);
			await LootMailbox();
			
			Log("Go to auctioneer");
			// await MoveTo(AH_WAYPOINT);
			await MoveTo(AUCTIONEER_LOCATION);
			await PostAuctions();

			milling = false;

			Log("Go to ink trader");
			await MoveTo(TRADER_LOCATION);
			await MillHerbs();
			await CreateInk();
			await TradeInks(_inkTraderId);
			if (Me.MapId == 571) // in dalaran, parchments are not sold by the ink trader
				await TradeInks(28723); // npc right beside
			await CraftGlyphs();
			
			Log("Go to auctioneer");
			// await MoveTo(AH_WAYPOINT);
			await MoveTo(AUCTIONEER_LOCATION);
			await PostAuctions();

            await MoveTo(MAILBOX_LOCATION);
			
			//int waiting = Rand.Next(20000, 120000);
			int waiting = 7200000;
			Log("Waiting for " + waiting/1000 + "s");
			await Buddy.Coroutines.Coroutine.Sleep(waiting);
            return false;
        }
		
		private async Task<bool> MillHerbs()
		{
			int c = 0;
			while (isHerbInBag())
			{
				await Buddy.Coroutines.Coroutine.Sleep(2000);
				Lua.DoString("RunMacroText('/click TSMDestroyButton')");
				Log("Milling herbs...");
				
				if (c > 70)
				{
					await Buddy.Coroutines.Coroutine.Sleep(1000);
					KeyboardManager.AntiAfk();
					KeyboardManager.KeyUpDown((char)Keys.Space);
					c = 0;
				}
				
				c++;
			}
			Log("No more herbs to mill");
			return false;
		}

		private async Task<bool> CreateInk()
		{
			int inksToCreate = getItemCount("Cerulean Pigment") / 2;
			if (inksToCreate < 10)
			{
				Log("Not enough pigments to create inks");
				return false;
			}
			
			Log("Creating " + inksToCreate.ToString() + " inks (" + (inksToCreate * 2).ToString() + "s)...");

			// Create inks by chunk of 100 to permit use of AntiAfk
			for (int i = 0; i < inksToCreate; i += 100)
			{
				int chunk = 0;

				if (inksToCreate - i < 100)
					chunk = inksToCreate - i;
				else
					chunk = 100;
					
				if (!isTSMInscriptionFrameOpened())
				{
					Log("Opening inscription panel");
					Lua.DoString("RunMacroText('/cast Inscription')");
					await Buddy.Coroutines.Coroutine.Sleep(2000);
				}
					
				string lua = "for i=1,GetNumTradeSkills() do if GetTradeSkillInfo(i)==\"Warbinder's Ink\" then CloseTradeSkill() DoTradeSkill(i, ";
				lua += chunk.ToString();
				lua += ") break end end";
				Lua.DoString(lua);
				
				await Buddy.Coroutines.Coroutine.Sleep(2050 * (chunk+1));
                KeyboardManager.KeyUpDown((char)Keys.Space);
				KeyboardManager.AntiAfk();
			}
			await Buddy.Coroutines.Coroutine.Sleep(5000);
			return false;
		}
		
		private async Task<bool> CraftGlyphs()
		{
			Log("Creating glyphs...");

			await populateGlyphsQueue();
			
			int c = 0;
			while (Lua.GetReturnVal<bool>("return TSMCraftNextButton:IsEnabled()", 0))
			{
				Log("Crafting...");
                // you have to bind a macro with /click TSMCraftNextButton to F10
                KeyboardManager.KeyUpDown((char)Keys.F10);
				await Buddy.Coroutines.Coroutine.Sleep(1000);
				
				// Wait 10s or until 
				if (!await Buddy.Coroutines.Coroutine.Wait(10000, () => Lua.GetReturnVal<bool>("return TSMCraftNextButton:IsEnabled()", 0)))
				{
					Log("No more glyphs to craft!");
					break;
				}
				
				await Buddy.Coroutines.Coroutine.Sleep(1000);
				
				if (isBagsFull())
				{
					Log("Bags full. Going to auctionneer to post glyphs...");
					// await MoveTo(AH_WAYPOINT);
					await MoveTo(AUCTIONEER_LOCATION);
					await PostAuctions();
					await Buddy.Coroutines.Coroutine.Sleep(2000);
					Log("Returning to ink trader...");
					await MoveTo(TRADER_LOCATION);
					return await CraftGlyphs();
				}
				
				if (c > 20)
				{
					await Buddy.Coroutines.Coroutine.Sleep(1000);
					KeyboardManager.AntiAfk();
					KeyboardManager.KeyUpDown((char)Keys.Space);
					await Buddy.Coroutines.Coroutine.Sleep(4000);
					c = 0;
				}
				
				c++;
			}
			return true;
		}
		
		public async Task<bool> MoveTo2(WoWPoint destination)
        {
			_gotoLocation = destination;
			
			while (_gotoLocation != WoWPoint.Zero)
				await Buddy.Coroutines.Coroutine.Sleep(2000);

			await Buddy.Coroutines.Coroutine.Sleep(2000);
			Log("Arrived to destination!");
            return true;
        }
		
		public async Task<bool> MoveTo(WoWPoint destination)
        {
			while (destination.DistanceSqr(Me.Location) > 16) {
				Navigator.MoveTo(destination);
				await Buddy.Coroutines.Coroutine.Sleep(10);
			}
			
			await Buddy.Coroutines.Coroutine.Sleep(2000);
			return true;
        }
		
		public async Task<bool> FlyTo(WoWPoint destination) {
			while (Flightor.MountHelper.CanMount && destination.DistanceSqr(Me.Location) > 16) {
				Flightor.MoveTo(destination);
				await Buddy.Coroutines.Coroutine.Sleep(10);
			}
			
			await Buddy.Coroutines.Coroutine.Sleep(2000);
			return true;
		}
		
		private async Task<bool> CancelUndercutAuctions()
		{
			Log("Cancel undercut auctions");
			if (!await initializeAH())
				return false;
			
			Log("Starting cancel scan...");
			Lua.DoString("RunMacroText('/click _TSMStartCancelScanButton')");
			await Buddy.Coroutines.Coroutine.Sleep(100);
            while (!isCancelScanDone())
            {
				await Buddy.Coroutines.Coroutine.Sleep(100);
                if (Lua.GetReturnVal<bool>("return TSMAuctioningCancelButton:IsEnabled()", 0))
                {
                    await Buddy.Coroutines.Coroutine.Sleep(100);
                    KeyboardManager.KeyUpDown((char)Keys.F8);
                    Log("Cancelling auction...");
                }
                await Buddy.Coroutines.Coroutine.Sleep(100);
                
            }

            await Buddy.Coroutines.Coroutine.Sleep(3000);
            Log("Undercut auctions cancelled!");

            return true;
		}
		
		private async Task<bool> PostAuctions()
		{
			Log("Post auctions");
			if (!await initializeAH())
				return false;
			
			Log("Starting post scan...");
			Lua.DoString("RunMacroText('/click _TSMStartPostScanButton')");
			await Buddy.Coroutines.Coroutine.Sleep(100);
            while (!isPostScanDone())
            {
				await Buddy.Coroutines.Coroutine.Sleep(100);
                if (Lua.GetReturnVal<bool>("return TSMAuctioningPostButton:IsEnabled()", 0))
                {
                    await Buddy.Coroutines.Coroutine.Sleep(100);
                    KeyboardManager.KeyUpDown((char)Keys.F9);
                    Log("Posting auction...");
                }
                await Buddy.Coroutines.Coroutine.Sleep(100);
            }

            await Buddy.Coroutines.Coroutine.Sleep(3000);
            Log("Auctions posted!");

            return true;
		}
		
		private async Task<bool> LootMailbox()
		{
			int numItems = 0;
			int totalItems = 0;
			
			do
			{
				Log("Looting mailbox...");
				var mailBox = ObjectManager.GetObjectsOfTypeFast<WoWGameObject>().FirstOrDefault(u => u.Entry == _mailboxId);
				mailBox.Interact();
				await Buddy.Coroutines.Coroutine.Sleep(2000);

				if (!isMailFrameOpened()) {
					Log("Mailbox not opened!");
					return false;
				}
				
				Lua.DoString("RunMacroText('/click _TSMOpenAllMailButton')");
				do
				{
					await Buddy.Coroutines.Coroutine.Sleep(2000);
					List<string> data = Lua.GetReturnValues("a,b=GetInboxNumItems();return a,b;");
					numItems = data[0].ToInt32();
					totalItems = data[1].ToInt32();
					Log(totalItems.ToString() + " items remaining...");
					
					if (isBagsFull())
					{
						Log("Bags full. Going to auctionneer to post glyphs...");
						// await MoveTo(AH_WAYPOINT);
						await MoveTo(AUCTIONEER_LOCATION);
						await PostAuctions();
						await Buddy.Coroutines.Coroutine.Sleep(2000);
						Log("Returning to mailbox...");
						await MoveTo(MAILBOX_LOCATION);
						return await LootMailbox();
					}
				} while (numItems > 0);
				
				if (totalItems > 0)
				{
					await Buddy.Coroutines.Coroutine.Sleep(2000);
					Lua.DoString("ReloadUI()");
					Log("Waiting 10s for UI to reload...");
					await Buddy.Coroutines.Coroutine.Sleep(10000);		// FIXME: check when in game StyxWoW.IsInGame
				}
				
			} while (totalItems > 0);
			
			Log("Finished looting mailbox!");

			return true;
		}
		
		private async Task<bool> TradeInks(int traderId)
		{
			Log("Trading inks...");
			
			await populateGlyphsQueue();
			
			Lua.DoString("RunMacroText('/click _TSMGatherButton')");
			await Buddy.Coroutines.Coroutine.Sleep(2000);
			Lua.DoString("RunMacroText('/click _TSMStartGatheringButton')");
			await Buddy.Coroutines.Coroutine.Sleep(2000);
			
			WoWUnit unit = ObjectManager.GetObjectsOfTypeFast<WoWUnit>().FirstOrDefault(u => u.Entry == traderId);	
			unit.Interact();
			await Buddy.Coroutines.Coroutine.Sleep(2000);

			if (!isVendorFrameOpened()) {
				Log("Vendor frame not opened!");
				return false;
			}
			
			Lua.DoString("RunMacroText('/click _TSMGatherItemsButton')");
			await Buddy.Coroutines.Coroutine.Sleep(6000);

			Log("Finished trading inks!");

			return true;
		}
		
		private async Task<bool> populateGlyphsQueue()
		{
			if (!isTSMInscriptionFrameOpened())
			{
				Log("Opening inscription panel");
				Lua.DoString("RunMacroText('/cast Inscription')");
				await Buddy.Coroutines.Coroutine.Sleep(2000);
			}
			Lua.DoString("RunMacroText('/click _TSMClearQueueButton')");
			await Buddy.Coroutines.Coroutine.Sleep(1000);
			Lua.DoString("RunMacroText('/click _TSMRestockButton')");
			await Buddy.Coroutines.Coroutine.Sleep(1000);
			return true;
		}
		
		private async Task<bool> initializeAH()
		{
            // reset tsm tweak
            Lua.DoString("AliasPostScan = false; AliasCancelScan = false");
			WoWUnit unit = ObjectManager.GetObjectsOfTypeFast<WoWUnit>().FirstOrDefault(u => u.Entry == _auctioneerId);		// Auctioneer Fitch, Stormwind main AH
			unit.Interact();
			await Buddy.Coroutines.Coroutine.Sleep(3000);
			if (!isAuctionFrameOpened()) {
				Log("Auction Frame not opened!");
				return false;
			}
			
			Lua.DoString("RunMacroText('/click AuctionFrameTab5')");
			await Buddy.Coroutines.Coroutine.Sleep(2000);
			return true;
		}
		
        private bool isHerbInBag()
        {
            return Lua.GetReturnVal<bool>("return TSMDestroyingFrame:IsVisible()", 0);
        }
		
		private bool isTSMInscriptionFrameOpened()
		{
			const string lua = "if not TSMCraftingTradeSkillFrame then return false; else return tostring(TSMCraftingTradeSkillFrame:IsVisible());end;";
            string t = Lua.GetReturnValues(lua)[0];
            return t.ToBoolean();
		}
		
		private bool isAuctionFrameOpened()
		{
			const string lua = "if not AuctionFrame then return false; else return tostring(AuctionFrame:IsVisible());end;";
            string t = Lua.GetReturnValues(lua)[0];
            return t.ToBoolean();
		}
		
		private bool isMailFrameOpened()
		{
			const string lua = "if not MailFrame then return false; else return tostring(MailFrame:IsVisible());end;";
            string t = Lua.GetReturnValues(lua)[0];
            return t.ToBoolean();
		}
		
		private bool isVendorFrameOpened()
		{
			const string lua = "if not MerchantFrame then return false; else return tostring(MerchantFrame:IsVisible());end;";
            string t = Lua.GetReturnValues(lua)[0];
            return t.ToBoolean();
		}
		
		private bool isBagsFull()
		{
			const string lua = "local s=0 for i=1,5 do s=s+GetContainerNumFreeSlots(i-1)end;return s";
            int t = Lua.GetReturnValues(lua)[0].ToInt32();
			if (t > 0)
				return false;
			else
				return true;
		}

        private bool isCancelScanDone()
        {
            const string lua = "return AliasCancelScan";
            return Lua.GetReturnVal<bool>(lua, 0);
        }

        private bool isPostScanDone()
        {
            const string lua = "return AliasPostScan";
            return Lua.GetReturnVal<bool>(lua, 0);
        }
		
		private int getItemCount(string item)
		{
			List<string> items = Lua.GetReturnValues("function wi() local ct=0;for i=0,4 do for j=1,GetContainerNumSlots(i) do local a,b,c,d,e,e,g=GetContainerItemInfo(i,j) if g and g:find(\"" + item + "\") then if b then ct=ct+b end end end end return ct end;return(tostring(wi()));");
			if (items != null && items.Count == 1)
				return items[0].ToInt32();
			else
				return 0;
		}
		
		public override void Pulse()
        {
            try
            {
                if (!StyxWoW.IsInGame)
                    return;
                
                if (_gotoLocation != WoWPoint.Zero) {
					if (Flightor.MountHelper.CanMount)
						// Flightor.MoveTo(_gotoLocation);				// FIXME: add waypoint to enable fly
						Navigator.MoveTo(_gotoLocation);
					else
						Navigator.MoveTo(_gotoLocation);

					if (_gotoLocation.DistanceSqr(Me.Location) <= 4 * 4) {
						_gotoLocation = WoWPoint.Zero;
						WoWMovement.MoveStop();
					}
				}

				if (milling && !Me.IsMoving)
				{
					if (isHerbInBag())
						Lua.DoString("RunMacroText('/click TSMDestroyButton')");
				}
					
			}
			catch (ThreadAbortException)
            {
            }
            catch (Exception e)
            {
                Log("Exception in Pulse:{0}", e);
            }
        }
		
        public override PulseFlags PulseFlags
        {
            get { return PulseFlags.All; }
        }

        public Version Version
        {
            get { return _version; }
        }

        public override void Start()
        {
        }

        public override void Stop()
        {
        }

        public override void Initialize()
        {
        }

        public void Log(string msg, params object[] args)
        {
            Logging.Write(Instance.Name + ": " + msg, args);
        }

        public void Log(Color c, string msg, params object[] args)
        {
            Logging.Write(c, msg, args);
        }
    }
}
