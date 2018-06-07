#!/usr/bin/perl


# Defaults
my %conf;
my %sensorname;
my %sensordesc;
@conf{"loop"} = 0;
@conf{"loopdwell"} = 1000;
@conf{"devicedir"} = "/sys/bus/w1/devices";

my @sensors;

# Read conf file if present

my $haveConf = open(my $CONF, "<", "./ds18b20.conf");

if (defined $haveConf) {
   while(<$CONF>) {
      next if (/^#/);
      next if (/^$/);
      chomp;
     ($stuff,$comment) = split(/#/);
     ($var,$val) = split(/=/,$stuff);
     if ($var ~~ [qr/^sensor/]) {
        ($name,$desc,$id) = split(/,/,$val);
        $sensorname{$id} = $name;
        $sensordesc{$id} = $desc;
     } else {
        $conf{$var} = $val;
     }
  }
  close($CONF);
} else {
  print "No config file found, using defaults\n";
}


if (%sensorname > 0) {
   foreach my $id (keys %sensorname) {
      my $file = $conf{"devicedir"}."/".$id."/w1_slave";
      my $sensorStatus = open(my $sensor,"<",$file);
      if (defined $sensorStatus) {
         while(<$sensor>) {
            if (/YES/) {
              # This is a good data reading, skip this line
              # then get the part after t=
              $_ = <$sensor>;
              ($bytes, $tempC) = split(/=/);
              chomp($tempC);
              $tempC = $tempC/1000.0;
              print $id." ".$sensorname{$id}." temp ".$tempC."C (".((9*($tempC)/5)+32)."F)\n";
            }
         }
      }
   }
} else {
   print "no sensors\n";
}

