#!/usr/bin/env python3
"""
Simple test script to simulate RFID tag reads for CrossMgr Interface testing.
This script connects to the CrossMgr Interface and sends simulated tag reads
to test the race duration and lap prediction features.
"""

import socket
import time
import random
from datetime import datetime

def connect_to_crossmgr(host='localhost', port=53135):
    """Connect to CrossMgr Interface"""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(10)
    try:
        sock.connect((host, port))
        print(f"Connected to CrossMgr Interface at {host}:{port}")
        return sock
    except Exception as e:
        print(f"Failed to connect: {e}")
        return None

def send_identification(sock):
    """Send reader identification"""
    identifier = "N0001TestReader-12345\r"
    sock.send(identifier.encode('ascii'))
    print(f"Sent identification: {identifier.strip()}")
    
    # Wait for GT command from server
    response = sock.recv(1024).decode('ascii')
    print(f"Received from server: {response.strip()}")
    
    # Send GT response
    now = datetime.now()
    gt_response = f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r"
    sock.send(gt_response.encode('ascii'))
    print(f"Sent GT response: {gt_response.strip()}")
    
    # Wait for S0000 command
    response = sock.recv(1024).decode('ascii')
    print(f"Received from server: {response.strip()}")
    print("Handshake complete!")

def simulate_race(sock, num_riders=15, race_duration_minutes=8):
    """Simulate a race with multiple riders"""
    print(f"\nStarting race simulation with {num_riders} riders for {race_duration_minutes} minutes")
    
    # Generate rider tags - only use main riders, no extra tags for cleaner testing
    riders = []
    for i in range(1, num_riders + 1):
        riders.append(f"RIDER{i:03d}")
    
    print(f"Test riders: {riders}")
    
    # Set more realistic lap times with smaller differences to reduce excessive lapping
    # For 8-minute race, aim for 35-40 second lap times so leaders get ~12-14 laps
    rider_base_lap_times = {}
    for i, rider in enumerate(riders):
        if i < 2:
            # Top 2 riders - very fast (35-37 seconds) - these will lap others late in race
            rider_base_lap_times[rider] = random.uniform(35, 37)
        elif i < 5:
            # Next 3 riders - competitive (37-39 seconds) - might lap slowest riders
            rider_base_lap_times[rider] = random.uniform(37, 39)
        elif i < 10:
            # Middle pack (39-41 seconds) - safe from lapping
            rider_base_lap_times[rider] = random.uniform(39, 41)
        else:
            # Back markers (41-43 seconds) - will get lapped in final quarter
            rider_base_lap_times[rider] = random.uniform(41, 43)
    
    # Make riders have different performance patterns - smaller changes to avoid excessive lapping
    rider_improvement_rates = {}
    rider_consistency = {}  # How much variation each rider has per lap
    
    for i, rider in enumerate(riders):
        if i == 0:  # First rider - consistent fast performer
            rider_improvement_rates[rider] = random.uniform(-0.1, 0.1)   # Very steady
            rider_consistency[rider] = random.uniform(1, 2)              # Very consistent
        elif i == 1:  # Second rider - slightly improving over race
            rider_improvement_rates[rider] = random.uniform(-0.2, 0.0)   # Getting slightly faster
            rider_consistency[rider] = random.uniform(1, 2)              # Very consistent
        elif i < 5:   # Next riders - small variations
            rider_improvement_rates[rider] = random.uniform(-0.1, 0.2)   # Small changes
            rider_consistency[rider] = random.uniform(1, 3)              # Good consistency
        elif i < 10:  # Middle pack - moderate variation
            rider_improvement_rates[rider] = random.uniform(-0.1, 0.3)   
            rider_consistency[rider] = random.uniform(2, 4)              # Moderate consistency
        else:         # Back markers - gradually slower (will get lapped late)
            rider_improvement_rates[rider] = random.uniform(0.1, 0.3)    # Getting slower
            rider_consistency[rider] = random.uniform(2, 5)              # Less consistent
    
    rider_last_crossing = {}
    rider_lap_details = {}  # Track position history for each rider
    
    # Select 1-2 riders to DNF at random points in the race
    num_dnf_riders = random.randint(1, 2)
    dnf_riders = random.sample(riders[5:], num_dnf_riders)  # Don't DNF the top 5 riders to keep race competitive
    dnf_lap_numbers = {}
    dnf_completed = set()
    
    for rider in dnf_riders:
        # DNF between lap 3 and 8 (so they get some laps in but DNF before race end)
        dnf_lap_numbers[rider] = random.randint(3, 8)
    
    race_start = time.time()
    race_end = race_start + (race_duration_minutes * 60)
    
    lap_number = {rider: 0 for rider in riders}
    
    print(f"\nRider profiles (8-minute race, lapping expected in final 2 minutes):")
    for i, rider in enumerate(riders):
        improvement = rider_improvement_rates.get(rider, 0)
        consistency = rider_consistency.get(rider, 3)
        profile = ""
        if i == 0:
            profile = "race leader (consistent)"
        elif i == 1:
            profile = "strong contender"
        elif i < 5:
            profile = "front pack"
        elif i < 10:
            profile = "middle pack (safe)"
        else:
            profile = "back markers (risk lapping)"
        
        print(f"  {rider}: {rider_base_lap_times[rider]:.1f}s base, {profile}")
    
    print(f"\nExpected race dynamics:")
    print(f"  - Leaders will complete ~13-14 laps in 8 minutes")
    print(f"  - Back markers will complete ~11-12 laps")
    print(f"  - Lapping should occur around minute 6-7 (75% race distance)")
    print(f"  - Only top 2-3 riders should lap the slowest 3-5 riders")
    
    print(f"\nDNF simulation:")
    for rider in dnf_riders:
        print(f"  - {rider} will DNF after completing lap {dnf_lap_numbers[rider]}")
    print(f"  - {num_dnf_riders} rider(s) selected for DNF testing")
    
    while time.time() < race_end:
        # Determine which rider should cross next
        current_time = time.time()
        
        for rider in riders:
            # Skip riders who have already DNF'd
            if rider in dnf_completed:
                continue
                
            # Calculate when this rider should cross next with more realistic variation
            base_lap_time = rider_base_lap_times[rider]
            consistency_factor = rider_consistency.get(rider, 3)
            
            # Add variation based on rider's consistency (good riders are more consistent)
            lap_time_variation = random.uniform(-consistency_factor, consistency_factor)
            lap_time = base_lap_time + lap_time_variation
            
            # Ensure minimum lap time (no unrealistic fast laps)
            lap_time = max(lap_time, base_lap_time * 0.8)
            
            if rider not in rider_last_crossing:
                # First crossing for this rider - smaller staggered start to keep field together
                skill_index = list(riders).index(rider)
                start_delay = random.uniform(0, 5 + skill_index * 1)  # Smaller start delays
                
                if current_time - race_start > start_delay:
                    rider_last_crossing[rider] = current_time
                    lap_number[rider] += 1
                    
                    # Calculate position for this lap
                    current_laps = [(r, lap_number.get(r, 0)) for r in riders]
                    current_laps.sort(key=lambda x: x[1], reverse=True)
                    position = next(i for i, (r, _) in enumerate(current_laps, 1) if r == rider)
                    
                    # Store lap details
                    if rider not in rider_lap_details:
                        rider_lap_details[rider] = []
                    rider_lap_details[rider].append({
                        'lap': lap_number[rider],
                        'position': position,
                        'race_time': current_time - race_start
                    })
                    
                    send_tag_read(sock, rider, lap_number[rider])
                    print(f"  P{position} {rider} completed lap {lap_number[rider]} (race start)")
                    
                    # Check if this rider should DNF after this lap
                    if rider in dnf_riders and lap_number[rider] >= dnf_lap_numbers[rider]:
                        dnf_completed.add(rider)
                        print(f"  >>> {rider} DNF after lap {lap_number[rider]} <<<")
            else:
                # Check if it's time for next lap
                time_since_last = current_time - rider_last_crossing[rider]
                if time_since_last >= lap_time:
                    rider_last_crossing[rider] = current_time
                    lap_number[rider] += 1
                    
                    # Calculate position for this lap
                    current_laps = [(r, lap_number.get(r, 0)) for r in riders]
                    current_laps.sort(key=lambda x: x[1], reverse=True)
                    position = next(i for i, (r, _) in enumerate(current_laps, 1) if r == rider)
                    
                    # Store lap details
                    if rider not in rider_lap_details:
                        rider_lap_details[rider] = []
                    rider_lap_details[rider].append({
                        'lap': lap_number[rider],
                        'position': position,
                        'race_time': current_time - race_start,
                        'lap_time': time_since_last
                    })
                    
                    send_tag_read(sock, rider, lap_number[rider])
                    
                    print(f"  P{position} {rider} completed lap {lap_number[rider]} (lap time: {time_since_last:.1f}s)")
                    
                    # Check if this rider should DNF after this lap
                    if rider in dnf_riders and lap_number[rider] >= dnf_lap_numbers[rider]:
                        dnf_completed.add(rider)
                        print(f"  >>> {rider} DNF after lap {lap_number[rider]} <<<")
                        continue  # Skip further processing for this rider
                    
                    # Smaller adjustment to base lap time (more realistic race progression)
                    improvement_rate = rider_improvement_rates.get(rider, 0)
                    rider_base_lap_times[rider] += random.uniform(-0.2, 0.3) + improvement_rate
        
        # Wait a bit before checking again
        time.sleep(1)
    
    print(f"\nRace simulation completed!")
    print("Final lap counts:")
    for rider in riders:
        dnf_status = " (DNF)" if rider in dnf_completed else ""
        print(f"  {rider}: {lap_number[rider]} laps{dnf_status}")
    
    if dnf_completed:
        print(f"\nDNF Summary:")
        for rider in dnf_completed:
            print(f"  - {rider} DNF after {lap_number[rider]} laps (planned DNF at lap {dnf_lap_numbers[rider]})")
    
    print(f"\nDetailed lap history with positions:")
    for rider in riders:
        dnf_marker = " (DNF)" if rider in dnf_completed else ""
        if rider in rider_lap_details:
            print(f"\n{rider}{dnf_marker}:")
            for lap_info in rider_lap_details[rider]:
                lap_time_str = ""
                if 'lap_time' in lap_info:
                    lap_time_str = f" (lap time: {lap_info['lap_time']:.1f}s)"
                race_time_str = f"{lap_info['race_time']:.1f}s"
                print(f"  Lap {lap_info['lap']}: P{lap_info['position']} at {race_time_str}{lap_time_str}")
        else:
            print(f"\n{rider}{dnf_marker}: No laps completed")

def send_tag_read(sock, tag_id, lap_count):
    """Send a DA tag read message"""
    now = datetime.now()
    time_str = now.strftime('%H:%M:%S.%f')[:-3]  # HH:MM:SS.fff
    count = f"{lap_count:05d}"
    date_str = now.strftime('%Y%m%d')
    
    message = f"DA{tag_id} {time_str} 10 {count} C7 date={date_str}\r"
    sock.send(message.encode('ascii'))

def main():
    print("CrossMgr Interface Race Simulation Test")
    print("=====================================")
    
    # Connect to the interface
    sock = connect_to_crossmgr()
    if not sock:
        return
    
    try:
        # Complete handshake
        send_identification(sock)
        
        # Wait a moment
        time.sleep(2)
        
        # Simulate a race to test realistic lapping in final quarter
        simulate_race(sock, num_riders=100, race_duration_minutes=12)
        
    except KeyboardInterrupt:
        print("\nTest interrupted by user")
    except Exception as e:
        print(f"Error during simulation: {e}")
    finally:
        sock.close()
        print("Connection closed")

if __name__ == "__main__":
    main()
