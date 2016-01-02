﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using System.Windows;
using Huoyaoyuan.AdmiralRoom.API;
using Huoyaoyuan.AdmiralRoom.Properties;

namespace Huoyaoyuan.AdmiralRoom.Officer
{
    public class Fleet : GameObject<getmember_deck>
    {
        public override int Id => rawdata.api_id;
        public string Name => rawdata.api_name;
        public FleetMissionState MissionState => (FleetMissionState)rawdata.api_mission[0];
        public int MissionID => (int)rawdata.api_mission[1];
        public MissionInfo MissionInfo => Staff.Current.MasterData.MissionInfo[MissionID];
        public DateTimeOffset BackTime { get; private set; }
        public TimeSpan ConditionTimeRemain => ConditionHelper.Instance.Remain(mincondition);
        public int ConditionTimeOffset => (int)ConditionHelper.Instance.Offset.TotalSeconds;

        #region Ships
        private ObservableCollection<Ship> _ships = new ObservableCollection<Ship>();
        public ObservableCollection<Ship> Ships
        {
            get { return _ships; }
            set
            {
                if (_ships != value)
                {
                    _ships = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        public Fleet()
        {
            WeakEventManager<Timer, ElapsedEventArgs>.AddHandler(Staff.Current.Ticker, "Elapsed", Tick);
            WeakEventManager<Timer, ElapsedEventArgs>.AddHandler(Staff.Current.Ticker, "Elapsed", CheckHomeportRepairing);
        }
        public Fleet(getmember_deck api) : base(api)
        {
            WeakEventManager<Timer, ElapsedEventArgs>.AddHandler(Staff.Current.Ticker, "Elapsed", Tick);
            WeakEventManager<Timer, ElapsedEventArgs>.AddHandler(Staff.Current.Ticker, "Elapsed", CheckHomeportRepairing);
        }
        private void Tick(object sender, ElapsedEventArgs e)
        {
            OnPropertyChanged(nameof(BackTime));
            OnPropertyChanged(nameof(ConditionTimeRemain));
            OnPropertyChanged(nameof(HomeportRepairingFrom));
            if (MissionState == FleetMissionState.InMission && Config.Current.NotifyWhenExpedition && BackTime.InASecond(Config.Current.NotifyTimeAdjust))
                Notifier.Current?.Show(Resources.Notification_Expedition_Title, string.Format(Resources.Notification_Expedition_Text, Name, MissionID, MissionInfo.Name), @"sound\expedition.mp3");
            if (!InSortie && Config.Current.NotifyWhenCondition && ConditionHelper.Instance.RemainCeiling(mincondition).InASecond())
                Notifier.Current?.Show(Resources.Notification_Condition_Title, string.Format(Resources.Notification_Condition_Text, Name), @"sound\condition.mp3");
        }

        public enum FleetMissionState { None = 0, InMission = 1, Complete = 2, Abort = 3 }

        #region NeedCharge
        private bool _needcharge;
        public bool NeedCharge
        {
            get { return _needcharge; }
            set
            {
                if (_needcharge != value)
                {
                    _needcharge = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region LowCondition
        private bool _lowcondition;
        public bool LowCondition
        {
            get { return _lowcondition; }
            set
            {
                if (_lowcondition != value)
                {
                    _lowcondition = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region HeavilyDamaged
        private bool _heavilydamaged;
        public bool HeavilyDamaged
        {
            get { return _heavilydamaged; }
            set
            {
                if (_heavilydamaged != value)
                {
                    _heavilydamaged = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Repairing
        private bool _repairing;
        public bool Repairing
        {
            get { return _repairing; }
            set
            {
                if (_repairing != value)
                {
                    _repairing = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Status
        private FleetStatus _status;
        public FleetStatus Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region InSortie
        private bool _insortie;
        public bool InSortie
        {
            get { return _insortie; }
            set
            {
                if (_insortie != value)
                {
                    _insortie = value;
                    UpdateStatus();
                }
            }
        }
        #endregion

        private bool needupdateship = false;
        protected override void UpdateProp()
        {
            BackTime = DateTimeOffset.FromUnixTimeMilliseconds(rawdata.api_mission[2]);
            needupdateship = false;
            for (int i = 0; i < rawdata.api_ship.Length; i++)
            {
                if (rawdata.api_ship[i] == -1)
                {
                    if (Ships.Count > i)
                    {
                        needupdateship = true;
                        break;
                    }
                }
                else
                {
                    if (Ships.Count <= i || Ships[i].Id != rawdata.api_ship[i] || Ships[i].InFleet != this)
                    {
                        needupdateship = true;
                        break;
                    }
                }
            }
            if (needupdateship)
                Ships = new ObservableCollection<Ship>(rawdata.api_ship
                    .Where(x => x != -1)
                    .Select(x =>
                    {
                        if (x == -1) return null;
                        Staff.Current.Homeport.Ships[x].InFleet = this;
                        return Staff.Current.Homeport.Ships[x];
                    }));
            UpdateStatus();
        }
        public int[] AirFightPower { get; private set; }
        public int LevelSum => Ships.Select(x => x.Level).Sum();
        public double LevelAverage => Ships.Any() ? Ships.Select(x => (double)x.Level).Average() : 0;
        public double LoSInMap => Ships.Select(x => x.LoSInMap).Sum() - Math.Ceiling(Staff.Current.Admiral.Level / 5.0) * 5.0 * 0.61;
        public int[] ChargeCost => new[]
        {
            Ships.Select(x => x.ApplyMarriage(x.Fuel.Shortage)).Sum(),
            Ships.Select(x => x.ApplyMarriage(x.Bull.Shortage)).Sum(),
            Ships.Select(x => x.Slots.Select(y => y.AirCraft.Shortage).Sum()).Sum() * 5
        };
        public int[] RepairCost => new[] { Ships.Select(x => x.RepairFuel).Sum(), Ships.Select(x => x.RepairSteel).Sum() };
        private int mincondition = 49;
        public void UpdateStatus()
        {
            bool f1 = false, f2 = false, f3 = false, f4 = false;
            foreach (var ship in Ships)
            {
                f1 |= ship.Condition < 40;
                f2 |= ship.HP.Current * 4 <= ship.HP.Max;
                f3 |= !(ship.Fuel.IsMax && ship.Bull.IsMax);
                f4 |= ship.IsRepairing;
            }
            LowCondition = f1;
            HeavilyDamaged = f2;
            NeedCharge = f3;
            Repairing = f4;
#pragma warning disable CC0014
            if (InSortie)
                Status = HeavilyDamaged ? FleetStatus.Warning : FleetStatus.InSortie;
            else if (MissionState != FleetMissionState.None)
                Status = FleetStatus.InMission;
            else if (NeedCharge || HeavilyDamaged || LowCondition || Repairing)
                Status = FleetStatus.NotReady;
            else Status = FleetStatus.Ready;
#pragma warning restore CC0014
            AirFightPower = Ships.Aggregate(new int[8], (x, y) => x.Zip(y.AirFightPower, (a, b) => a + b).ToArray());
            if (Ships.Any())
                mincondition = Ships.Select(x => x.Condition).Min();
            else
            {
                mincondition = 49;
                Status = FleetStatus.Empty;
            }
            OnPropertyChanged(nameof(AirFightPower));
            OnPropertyChanged(nameof(LevelSum));
            OnPropertyChanged(nameof(LevelAverage));
            OnPropertyChanged(nameof(LoSInMap));
            OnPropertyChanged(nameof(ChargeCost));
            OnPropertyChanged(nameof(RepairCost));
            OnPropertyChanged(nameof(CanHomeportRepairing));
        }

        protected override void OnAllPropertyChanged()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(MissionState));
            OnPropertyChanged(nameof(MissionID));
            OnPropertyChanged(nameof(MissionInfo));
            OnPropertyChanged(nameof(BackTime));
            OnPropertyChanged(nameof(ConditionTimeRemain));
            OnPropertyChanged(nameof(ConditionTimeOffset));
            if (needupdateship) OnPropertyChanged(nameof(Ships));
        }

#pragma warning disable CC0049
        public bool CanHomeportRepairing
            => Ships.Count > 0
            && Ships[0].ShipInfo.ShipType.Id == 19
            && Ships[0].IsRepairing == false
            && MissionState == FleetMissionState.None;
#pragma warning restore CC0049
        private IEnumerable<Ship> HomeportRepairingList => Ships.Take(
            CanHomeportRepairing ? Ships[0].Slots.Count(x => x.Item?.EquipInfo.EquipType.Id == 31) + 2 : 0)
            .Where(x => x.HP.Current * 2 > x.HP.Max);

        public DateTimeOffset HomeportRepairingFrom { get; private set; }
        public void CheckHomeportRepairingTime(bool reset)
        {
            var time = DateTimeOffset.UtcNow;
            if (reset || (time - HomeportRepairingFrom).TotalMinutes >= 20) HomeportRepairingFrom = time;
            CheckHomeportRepairing(null, null);
        }

        private void CheckHomeportRepairing(object sender, ElapsedEventArgs e)
        {
            var during = DateTimeOffset.UtcNow - HomeportRepairingFrom;
            if (during.TotalMinutes < 20) return;
            foreach (var ship in HomeportRepairingList)
                if (!ship.IsRepairing)
                    ship.RepairingHP = ship.HP.Current + Math.Max((int)((during.TotalSeconds - 30) / ship.RepairTimePerHP.TotalSeconds), 1);
        }
    }
    public enum FleetStatus { Empty, Ready, NotReady, InSortie, InMission, Warning }
}
