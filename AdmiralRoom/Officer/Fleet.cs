﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using Huoyaoyuan.AdmiralRoom.API;

namespace Huoyaoyuan.AdmiralRoom.Officer
{
    public class Fleet : GameObject<getmember_deck>, IDisposable
    {

        public override int Id => rawdata.api_id;
        public string Name => rawdata.api_name;
        public FleetMissionState MissionState => (FleetMissionState)rawdata.api_mission[0];
        public int MissionID => (int)rawdata.api_mission[1];
        public MissionInfo MissionInfo => Staff.Current.MasterData.MissionInfo[MissionID];
        public DateTime BackTime { get; private set; }
        public DateTime BackTimeLocal => BackTime.ToLocalTime();
        public TimeSpan BackTimeRemain => BackTime.Remain();

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

        public Fleet() { }
        public Fleet(getmember_deck api) : base(api)
        {
            Staff.Current.Ticker.Elapsed += Tick;
        }
        public void Dispose() => Staff.Current.Ticker.Elapsed -= Tick;
        private void Tick(object sender, ElapsedEventArgs e) => OnPropertyChanged("BackTimeRemain");

        public enum FleetMissionState { None = 0, InMission = 1, Complete = 2, Abort = 3 }

        private bool needupdateship = false;
        protected override void UpdateProp()
        {
            BackTime = DateTimeEx.FromUnixTime(rawdata.api_mission[2]);
            needupdateship = false;
            for(int i = 0; i < rawdata.api_ship.Length; i++)
            {
                if (rawdata.api_ship[i] == -1)
                {
                    if(Ships.Count > i)
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
                Ships = new ObservableCollection<Ship>(rawdata.api_ship.ArrayOperation(x =>
                {
                    if (x == -1) return null;
                    Staff.Current.Homeport.Ships[x].InFleet = this;
                    return Staff.Current.Homeport.Ships[x];
                }));
        }
        
        protected override void OnAllPropertyChanged()
        {
            OnPropertyChanged("Name");
            OnPropertyChanged("MissionState");
            OnPropertyChanged("MissionID");
            OnPropertyChanged("MissionInfo");
            OnPropertyChanged("BackTime");
            OnPropertyChanged("BackTimeLocal");
            if (needupdateship) OnPropertyChanged("Ships");
        }
    }
}
