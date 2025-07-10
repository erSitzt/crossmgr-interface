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

def simulate_race(sock, num_riders=15, race_duration_minutes=5):
    """Simulate a race with multiple riders"""
    print(f"\nStarting race simulation with {num_riders} riders for {race_duration_minutes} minutes")
    
    # Generate rider tags - mix of expected and unexpected tags for filter testing
    riders = []
    for i in range(1, num_riders + 1):
        if i <= 3:
            riders.append(f"RIDER{i:03d}")  # Expected tags that should pass filter
        else:
            riders.append(f"BIKE{i:03d}")   # Different prefix for filter testing
    
    # Add some random tags that should be filtered
    riders.extend(["RFID12345", "TAG999", "UNKNOWN01"])
    
    print(f"Test riders: {riders}")
    print("Expected tags (RIDER*): RIDER001, RIDER002, RIDER003")
    print("Other tags (should be filtered if prefix 'RIDER' is set): BIKE004, BIKE005, RFID12345, TAG999, UNKNOWN01")
    
    # Set random base lap times between 30-40 seconds for each rider
    rider_base_lap_times = {rider: random.uniform(30, 40) for rider in riders}
    
    # Make some riders get faster over time (simulate stronger finishers)
    rider_improvement_rates = {}
    for i, rider in enumerate(riders):
        if i < 2:  # First 2 riders get stronger over time (potential overtakers)
            rider_improvement_rates[rider] = random.uniform(-0.5, -0.2)  # Getting significantly faster
        elif i < 4:  # Next 2 riders are steady strong performers
            rider_improvement_rates[rider] = random.uniform(-0.2, 0.1)   # Slightly improving to steady
        else:
            rider_improvement_rates[rider] = random.uniform(0.0, 0.3)    # Getting slower (fatigue)
    
    rider_last_crossing = {}
    
    race_start = time.time()
    race_end = race_start + (race_duration_minutes * 60)
    
    lap_number = {rider: 0 for rider in riders}
    
    print(f"\nRider base lap times:")
    for rider in riders:
        improvement = rider_improvement_rates.get(rider, 0)
        improvement_str = "improving" if improvement < 0 else "tiring" if improvement > 0 else "steady"
        print(f"  {rider}: {rider_base_lap_times[rider]:.1f}s base ({improvement_str})")
    
    while time.time() < race_end:
        # Determine which rider should cross next
        current_time = time.time()
        
        for rider in riders:
            # Calculate when this rider should cross next
            base_lap_time = rider_base_lap_times[rider]
            # Add variation of 1-5 seconds per lap
            lap_time = base_lap_time + random.uniform(1, 5)
            
            if rider not in rider_last_crossing:
                # First crossing for this rider
                if current_time - race_start > random.uniform(0, 30):  # Staggered start
                    rider_last_crossing[rider] = current_time
                    lap_number[rider] += 1
                    send_tag_read(sock, rider, lap_number[rider])
                    print(f"  {rider} completed lap {lap_number[rider]}")
            else:
                # Check if it's time for next lap
                time_since_last = current_time - rider_last_crossing[rider]
                if time_since_last >= lap_time:
                    rider_last_crossing[rider] = current_time
                    lap_number[rider] += 1
                    send_tag_read(sock, rider, lap_number[rider])
                    print(f"  {rider} completed lap {lap_number[rider]} (lap time: {time_since_last:.1f}s)")
                    
                    # Slightly adjust base lap time for next lap (simulation of fatigue/improvement)
                    improvement_rate = rider_improvement_rates.get(rider, 0)
                    rider_base_lap_times[rider] += random.uniform(-1, 2) + improvement_rate
        
        # Wait a bit before checking again
        time.sleep(1)
    
    print(f"\nRace simulation completed!")
    print("Final lap counts:")
    for rider in riders:
        print(f"  {rider}: {lap_number[rider]} laps")

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
        
        # Simulate a race to test leader changes during additional laps
        simulate_race(sock, num_riders=8, race_duration_minutes=5)
        
    except KeyboardInterrupt:
        print("\nTest interrupted by user")
    except Exception as e:
        print(f"Error during simulation: {e}")
    finally:
        sock.close()
        print("Connection closed")

if __name__ == "__main__":
    main()
